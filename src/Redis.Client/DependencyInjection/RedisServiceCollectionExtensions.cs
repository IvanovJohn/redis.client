using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Redis.Client;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IRedisClient"/> in a service collection.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>Registers a singleton <see cref="IRedisClient"/> configured by a delegate.</summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Configures the options.</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddRedisClient(this IServiceCollection services, Action<RedisClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Add(services, key: null, options => options.Configure(configure));
    }

    /// <summary>Registers a singleton <see cref="IRedisClient"/> from a connection string.</summary>
    /// <param name="services">The services.</param>
    /// <param name="connectionString">A <c>redis://</c> or <c>rediss://</c> URI.</param>
    /// <param name="configure">Further configuration (e.g. TLS callbacks, credentials provider).</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddRedisClient(this IServiceCollection services, string connectionString, Action<RedisClientOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        return Add(services, key: null, options => options.Configure(o =>
        {
            o.ApplyConnectionString(connectionString);
            configure?.Invoke(o);
        }));
    }

    /// <summary>
    /// Registers a singleton <see cref="IRedisClient"/> bound to a configuration section. The section may contain
    /// <c>ConnectionString</c>, <c>Endpoints</c> (<c>host:port</c> list) and any property of
    /// <see cref="RedisClientOptions"/> by name; unknown keys are rejected so that typos fail at startup.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="section">The configuration section, e.g. <c>configuration.GetSection("Redis")</c>.</param>
    /// <param name="configure">Further configuration applied after the section.</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddRedisClient(this IServiceCollection services, IConfiguration section, Action<RedisClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        return Add(services, key: null, options => Bind(options, section, configure));
    }

    /// <summary>Registers a keyed singleton <see cref="IRedisClient"/> configured by a delegate.</summary>
    /// <param name="services">The services.</param>
    /// <param name="key">The service key, e.g. <c>"cache"</c>; also the name of its options.</param>
    /// <param name="configure">Configures the options.</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddKeyedRedisClient(this IServiceCollection services, string key, Action<RedisClientOptions> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(configure);
        return Add(services, key, options => options.Configure(configure));
    }

    /// <summary>Registers a keyed singleton <see cref="IRedisClient"/> from a connection string.</summary>
    /// <param name="services">The services.</param>
    /// <param name="key">The service key; also the name of its options.</param>
    /// <param name="connectionString">A <c>redis://</c> or <c>rediss://</c> URI.</param>
    /// <param name="configure">Further configuration.</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddKeyedRedisClient(this IServiceCollection services, string key, string connectionString, Action<RedisClientOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        return Add(services, key, options => options.Configure(o =>
        {
            o.ApplyConnectionString(connectionString);
            configure?.Invoke(o);
        }));
    }

    /// <summary>Registers a keyed singleton <see cref="IRedisClient"/> bound to a configuration section.</summary>
    /// <param name="services">The services.</param>
    /// <param name="key">The service key; also the name of its options.</param>
    /// <param name="section">The configuration section.</param>
    /// <param name="configure">Further configuration applied after the section.</param>
    /// <returns>The services, for chaining.</returns>
    public static IServiceCollection AddKeyedRedisClient(this IServiceCollection services, string key, IConfiguration section, Action<RedisClientOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(section);
        return Add(services, key, options => Bind(options, section, configure));
    }

    private static IServiceCollection Add(IServiceCollection services, string? key, Action<OptionsBuilder<RedisClientOptions>> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        var name = key ?? Options.Options.DefaultName;
        configure(services.AddOptions<RedisClientOptions>(name));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RedisClientOptions>, RedisClientOptionsValidator>());

        if (key is null)
        {
            services.TryAddSingleton<IRedisClient>(sp => Create(sp, name));
        }
        else
        {
            services.TryAddKeyedSingleton<IRedisClient>(key, (sp, _) => Create(sp, name));
        }

        return services;
    }

    private static RedisClient Create(IServiceProvider services, string name) =>
        new(
            services.GetRequiredService<IOptionsMonitor<RedisClientOptions>>().Get(name),
            services.GetService<ILoggerFactory>(),
            services.GetService<IMeterFactory>());

    private static void Bind(OptionsBuilder<RedisClientOptions> builder, IConfiguration section, Action<RedisClientOptions>? configure)
    {
        builder.Configure(options =>
        {
            ApplySection(options, section);
            configure?.Invoke(options);
        });
        builder.Services.AddSingleton<IOptionsChangeTokenSource<RedisClientOptions>>(new SectionChangeTokenSource(builder.Name, section));
    }

    /// <summary>Applies a configuration section without reflection (trimming/AOT safe).</summary>
    private static void ApplySection(RedisClientOptions options, IConfiguration section)
    {
        if (section["ConnectionString"] is { Length: > 0 } connectionString)
        {
            options.ApplyConnectionString(connectionString);
        }

        foreach (var child in section.GetChildren())
        {
            switch (child.Key.ToLowerInvariant())
            {
                case "connectionstring":
                    break;
                case "endpoints":
                    options.Endpoints.Clear();
                    foreach (var endpoint in child.GetChildren().Select(c => c.Value).Append(child.Value).OfType<string>())
                    {
                        options.Endpoints.Add(ParseEndpoint(endpoint));
                    }

                    break;
                case "username":
                    options.Username = child.Value;
                    break;
                case "password":
                    options.Password = child.Value;
                    break;
                case "ssl":
                    options.Ssl = bool.TryParse(child.Value, out var ssl) ? ssl : throw new FormatException("Invalid value of Redis option 'Ssl': expected true or false.");
                    break;
                default:
                    if (child.Value is null)
                    {
                        throw new FormatException($"Redis option '{child.Key}' must be a single value.");
                    }

                    ConnectionStringParser.ApplyOption(child.Key, child.Value, options);
                    break;
            }
        }
    }

    private static EndPoint ParseEndpoint(string text)
    {
        var colon = text.LastIndexOf(':');
        var host = colon > 0 && !text.EndsWith(']') ? text[..colon] : text;
        var port = colon > 0 && !text.EndsWith(']') && int.TryParse(text[(colon + 1)..], out var p) ? p : RedisClientOptions.DefaultPort;
        host = host.Trim('[', ']');
        return IPAddress.TryParse(host, out var address) ? new IPEndPoint(address, port) : new DnsEndPoint(host, port);
    }

    private sealed class SectionChangeTokenSource(string name, IConfiguration section) : IOptionsChangeTokenSource<RedisClientOptions>
    {
        public string Name { get; } = name;

        public IChangeToken GetChangeToken() => section.GetReloadToken();
    }
}
