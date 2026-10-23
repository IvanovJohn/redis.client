using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Redis.Client.Comparison;

/// <summary>Minimal client for the toxiproxy HTTP API plus the docker controls the scenarios need.</summary>
internal sealed class Toxiproxy(Uri api, string proxyName, string listen, string upstream) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = api };

    /// <summary>Creates the proxy, or resets it (enabled, no toxics) when it already exists.</summary>
    public async Task EnsureProxyAsync()
    {
        var response = await _http.PostAsJsonAsync("/proxies", new JsonObject
        {
            ["name"] = proxyName,
            ["listen"] = listen,
            ["upstream"] = upstream,
            ["enabled"] = true,
        });
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            (await _http.PostAsync("/reset", null)).EnsureSuccessStatusCode();
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task AddToxicAsync(string name, string type, JsonObject attributes, string stream = "downstream")
    {
        var response = await _http.PostAsJsonAsync($"/proxies/{proxyName}/toxics", new JsonObject
        {
            ["name"] = name,
            ["type"] = type,
            ["stream"] = stream,
            ["toxicity"] = 1.0,
            ["attributes"] = attributes,
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveToxicAsync(string name) =>
        (await _http.DeleteAsync($"/proxies/{proxyName}/toxics/{name}")).EnsureSuccessStatusCode();

    /// <summary>Disabling closes every proxied connection and refuses new ones.</summary>
    public async Task SetEnabledAsync(bool enabled) =>
        (await _http.PostAsJsonAsync($"/proxies/{proxyName}", new JsonObject { ["enabled"] = enabled })).EnsureSuccessStatusCode();

    public static async Task DockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {string.Join(' ', arguments)}: {await process.StandardError.ReadToEndAsync()}");
        }
    }

    public void Dispose() => _http.Dispose();
}
