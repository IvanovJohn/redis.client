using Redis.Client.Connections;
using Redis.Client.Protocol;

namespace Redis.Client.Transactions;

/// <summary>A command queued in a transaction; its result is set from the <c>EXEC</c> reply.</summary>
internal abstract class QueuedCommand(RedisCommand command)
{
    public RedisCommand Command { get; } = command;

    public abstract void SetResult(RespFrame frame);

    public abstract void SetException(Exception exception);

    public abstract void SetCanceled();
}

internal sealed class QueuedCommand<T>(RedisCommand command, Func<RespFrame, T> processor) : QueuedCommand(command)
{
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> Task => _completion.Task;

    public override void SetResult(RespFrame frame)
    {
        if (frame.IsError)
        {
            _completion.TrySetException(frame.ToServerException());
            return;
        }

        try
        {
            _completion.TrySetResult(processor(frame));
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
        }
    }

    public override void SetException(Exception exception) => _completion.TrySetException(exception);

    public override void SetCanceled() => _completion.TrySetCanceled();
}

/// <summary>
/// <c>MULTI</c>, the queued commands and <c>EXEC</c>, written as one contiguous block. It expects one reply to
/// <c>MULTI</c>, one <c>QUEUED</c> (or error) per command and the <c>EXEC</c> reply. The task result is
/// <see langword="false"/> when <c>EXEC</c> returns nil because a watched key changed.
/// </summary>
internal sealed class PendingTransaction : PendingOperation
{
    private readonly IReadOnlyList<QueuedCommand> _commands;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _received;
    private RedisServerException? _queueError;

    public PendingTransaction(IReadOnlyList<QueuedCommand> commands, TimeSpan timeout)
        : base(timeout)
    {
        _commands = commands;
    }

    public override string Name => "EXEC";

    public Task<bool> Task => _completion.Task;

    public override bool IsCompleted => _completion.Task.IsCompleted;

    public override void Write(PhysicalConnection connection)
    {
        connection.Write(new RedisCommand(Commands.Multi));
        foreach (var queued in _commands)
        {
            connection.Write(queued.Command);
        }

        connection.Write(new RedisCommand(Commands.Exec));
    }

    public override bool OnFrame(RespFrame frame)
    {
        var index = _received++;
        var execIndex = _commands.Count + 1;
        if (index < execIndex)
        {
            // Reply to MULTI or QUEUED for a command; remember the first error for the caller.
            if (frame.IsError)
            {
                _queueError ??= frame.ToServerException();
            }

            return false;
        }

        if (_completion.Task.IsCompleted)
        {
            return true; // timed out or cancelled while waiting: discard
        }

        if (frame.IsError)
        {
            var error = frame.ToServerException();
            foreach (var queued in _commands)
            {
                queued.SetException(error);
            }

            TrySetException(error);
            return true;
        }

        if (frame.IsNull)
        {
            // WATCH detected a change: nothing was executed.
            foreach (var queued in _commands)
            {
                queued.SetCanceled();
            }

            if (_completion.TrySetResult(false))
            {
                OnCompleted();
            }

            return true;
        }

        var i = 0;
        foreach (var result in frame.GetChildren())
        {
            if (i < _commands.Count)
            {
                _commands[i++].SetResult(result);
            }
        }

        if (_completion.TrySetResult(true))
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

        foreach (var queued in _commands)
        {
            queued.SetException(exception);
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

        foreach (var queued in _commands)
        {
            queued.SetCanceled();
        }

        OnCompleted();
        return true;
    }

    /// <summary>The first error returned while queuing (EXEC then fails with EXECABORT).</summary>
    public RedisServerException? QueueError => _queueError;
}
