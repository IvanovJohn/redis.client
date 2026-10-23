namespace Redis.Client.Connections;

/// <summary>Exponential reconnect delay with jitter, bounded by <see cref="RedisClientOptions.ReconnectMaxDelay"/>.</summary>
internal static class Backoff
{
    /// <summary>
    /// Delay before reconnect attempt number <paramref name="attempt"/> (1-based): a random value between half and
    /// all of <c>min(max, base * 2^(attempt-1))</c>, so concurrent clients do not reconnect in lockstep.
    /// </summary>
    public static TimeSpan GetDelay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 30);
        var ceiling = Math.Min(maxDelay.TotalMilliseconds, baseDelay.TotalMilliseconds * Math.Pow(2, exponent));
        var jittered = (ceiling / 2) + (Random.Shared.NextDouble() * ceiling / 2);
        return TimeSpan.FromMilliseconds(Math.Max(1, jittered));
    }
}
