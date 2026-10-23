using Microsoft.Extensions.Options;

namespace Redis.Client;

/// <summary>Validates <see cref="RedisClientOptions"/> before any connection attempt.</summary>
public sealed class RedisClientOptionsValidator : IValidateOptions<RedisClientOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RedisClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.Endpoints.Count == 0)
        {
            failures.Add($"{nameof(options.Endpoints)} must contain the Redis server endpoint.");
        }
        else if (options.Endpoints.Count > 1)
        {
            failures.Add($"{nameof(options.Endpoints)} contains {options.Endpoints.Count} endpoints, but multiple endpoints (Sentinel, Cluster) are not supported yet; specify exactly one.");
        }

        if (options.Endpoints.Any(e => e is null))
        {
            failures.Add($"{nameof(options.Endpoints)} must not contain null.");
        }

        Positive(failures, nameof(options.ConnectTimeout), options.ConnectTimeout);
        Positive(failures, nameof(options.CommandTimeout), options.CommandTimeout);
        Positive(failures, nameof(options.KeepAliveInterval), options.KeepAliveInterval);
        Positive(failures, nameof(options.ReconnectBaseDelay), options.ReconnectBaseDelay);
        Positive(failures, nameof(options.ReconnectMaxDelay), options.ReconnectMaxDelay);
        if (options.ReconnectBaseDelay > options.ReconnectMaxDelay)
        {
            failures.Add($"{nameof(options.ReconnectBaseDelay)} must not be greater than {nameof(options.ReconnectMaxDelay)}.");
        }

        if (options.Database < 0)
        {
            failures.Add($"{nameof(options.Database)} must be zero or greater.");
        }

        AtLeastOne(failures, nameof(options.MultiplexedConnectionCount), options.MultiplexedConnectionCount);
        AtLeastOne(failures, nameof(options.DedicatedPoolSize), options.DedicatedPoolSize);
        AtLeastOne(failures, nameof(options.SubscriptionBufferSize), options.SubscriptionBufferSize);
        AtLeastOne(failures, nameof(options.MaxNestingDepth), options.MaxNestingDepth);
        if (options.MaxBulkLength < 1)
        {
            failures.Add($"{nameof(options.MaxBulkLength)} must be at least 1.");
        }

        if (options.Password is not null && options.CredentialsProvider is not null)
        {
            failures.Add($"Set either {nameof(options.Password)} or {nameof(options.CredentialsProvider)}, not both.");
        }

        if (options.Username is not null && options.Password is null)
        {
            failures.Add($"{nameof(options.Username)} requires {nameof(options.Password)}.");
        }

        if (options.SslOptions is not null && !options.Ssl)
        {
            failures.Add($"{nameof(options.SslOptions)} is set but {nameof(options.Ssl)} is disabled.");
        }

        Defined(failures, nameof(options.Protocol), options.Protocol);
        Defined(failures, nameof(options.BacklogPolicy), options.BacklogPolicy);
        Defined(failures, nameof(options.SubscriptionOverflowPolicy), options.SubscriptionOverflowPolicy);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Throws <see cref="OptionsValidationException"/> if the options are invalid.</summary>
    internal static void ThrowIfInvalid(RedisClientOptions options, string? name = null)
    {
        var result = new RedisClientOptionsValidator().Validate(name, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(name ?? Options.DefaultName, typeof(RedisClientOptions), result.Failures);
        }
    }

    private static void Positive(List<string> failures, string name, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{name} must be greater than zero.");
        }
    }

    private static void AtLeastOne(List<string> failures, string name, int value)
    {
        if (value < 1)
        {
            failures.Add($"{name} must be at least 1.");
        }
    }

    private static void Defined<TEnum>(List<string> failures, string name, TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            failures.Add($"{name} has an unknown value '{value}'.");
        }
    }
}
