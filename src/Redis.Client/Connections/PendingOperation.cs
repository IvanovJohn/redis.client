using Redis.Client.Protocol;

namespace Redis.Client.Connections;

/// <summary>
/// A unit of work sent to the server as one contiguous block (a command, or a whole MULTI/EXEC transaction) and
/// the replies it expects.
/// </summary>
/// <remarks>
/// Lifecycle: <c>Queued</c> → <c>Written</c> (bytes handed to the socket) → completed. An operation that times out
/// or is cancelled while queued is completed without ever being written. One that times out after being written is
/// completed with an error but stays in the connection's reply queue, so that its late reply is consumed and
/// discarded instead of being delivered to the next caller.
/// </remarks>
internal abstract class PendingOperation
{
    private const int StateQueued = 0;
    private const int StateWritten = 1;
    private const int StateCompletedUnwritten = 2;

    private int _state;
    private CancellationTokenRegistration _cancellation;

    protected PendingOperation(TimeSpan timeout)
    {
        Timeout = timeout;
        Deadline = timeout == System.Threading.Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : Environment.TickCount64 + (long)Math.Ceiling(timeout.TotalMilliseconds);
    }

    /// <summary>Command name (or <c>MULTI</c> for transactions) for diagnostics.</summary>
    public abstract string Name { get; }

    public TimeSpan Timeout { get; }

    /// <summary>Deadline in <see cref="Environment.TickCount64"/> milliseconds.</summary>
    public long Deadline { get; }

    public bool IsWritten => Volatile.Read(ref _state) == StateWritten;

    public abstract bool IsCompleted { get; }

    /// <summary>Claims the operation for writing; fails if it was completed (timed out or cancelled) while queued.</summary>
    public bool TryMarkWritten() => Interlocked.CompareExchange(ref _state, StateWritten, StateQueued) == StateQueued;

    /// <summary>Completes an operation that has not been written; returns <see langword="false"/> if it was already written.</summary>
    public bool TryFailUnwritten(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _state, StateCompletedUnwritten, StateQueued) != StateQueued)
        {
            return false;
        }

        TrySetException(exception);
        return true;
    }

    /// <summary>Serializes the operation into the connection's write buffer.</summary>
    public abstract void Write(PhysicalConnection connection);

    /// <summary>
    /// Handles one reply frame. Returns <see langword="true"/> when the operation has received all replies it
    /// expects and must be removed from the reply queue. Called even after the caller was completed (abandoned).
    /// </summary>
    public abstract bool OnFrame(RespFrame frame);

    public abstract bool TrySetException(Exception exception);

    public abstract bool TrySetCanceled(CancellationToken cancellationToken);

    /// <summary>Completes the operation with a timeout if its deadline has passed.</summary>
    /// <returns><see langword="true"/> if this call timed the operation out.</returns>
    public bool TryExpire(long now)
    {
        if (now < Deadline || IsCompleted)
        {
            return false;
        }

        if (TryFailUnwritten(new RedisTimeoutException($"{Name} was not sent within {Timeout}: the connection is unavailable or busy.", mayHaveExecuted: false)))
        {
            return true;
        }

        return TrySetException(new RedisTimeoutException($"No reply to {Name} within {Timeout}. The command was sent and may have been executed.", mayHaveExecuted: true));
    }

    public void RegisterCancellation(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            _cancellation = cancellationToken.UnsafeRegister(static (state, token) => ((PendingOperation)state!).OnCanceled(token), this);
        }
    }

    /// <summary>Must be called by implementations when the caller-visible result is set.</summary>
    protected void OnCompleted() => _cancellation.Unregister();

    private void OnCanceled(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _state, StateCompletedUnwritten, StateQueued) == StateQueued)
        {
            TrySetCanceled(token);
            return;
        }

        // Already written: the reply will still arrive and be discarded.
        TrySetCanceled(token);
    }
}

/// <summary>A single command whose reply is converted by a processor.</summary>
internal sealed class PendingCommand<T> : PendingOperation
{
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<RespFrame, T> _processor;

    public PendingCommand(RedisCommand command, Func<RespFrame, T> processor, TimeSpan timeout)
        : base(timeout)
    {
        Command = command;
        _processor = processor;
    }

    public RedisCommand Command { get; }

    public override string Name => Command.Metadata.Name;

    public Task<T> Task => _completion.Task;

    public override bool IsCompleted => _completion.Task.IsCompleted;

    public override void Write(PhysicalConnection connection) => connection.Write(Command);

    public override bool OnFrame(RespFrame frame)
    {
        if (_completion.Task.IsCompleted)
        {
            return true;
        }

        if (frame.IsError)
        {
            TrySetException(frame.ToServerException());
            return true;
        }

        T result;
        try
        {
            result = _processor(frame);
        }
        catch (Exception ex)
        {
            TrySetException(ex);
            return true;
        }

        if (_completion.TrySetResult(result))
        {
            OnCompleted();
        }

        return true;
    }

    public override bool TrySetException(Exception exception)
    {
        if (!_completion.TrySetException(exception))
        {
            return false;
        }

        OnCompleted();
        return true;
    }

    public override bool TrySetCanceled(CancellationToken cancellationToken)
    {
        if (!_completion.TrySetCanceled(cancellationToken))
        {
            return false;
        }

        OnCompleted();
        return true;
    }

    public override string ToString() => Command.ToString();
}
