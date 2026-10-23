using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Redis.Client.Comparison;

// Usage: dotnet run -c Release -- [perf|chaos|all] [--quick] [--only rst,down,freeze,slow,slicer,restart]
// Requires `docker compose up -d` in this directory (redis + toxiproxy).
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var mode = args.FirstOrDefault(a => a is "perf" or "chaos" or "all") ?? "all";
var quick = args.Contains("--quick");
var onlyIndex = Array.IndexOf(args, "--only");
var onlyPhases = onlyIndex >= 0 && onlyIndex + 1 < args.Length ? args[onlyIndex + 1].Split(',') : null;

var direct = IPEndPoint.Parse("127.0.0.1:6390");
var proxied = IPEndPoint.Parse("127.0.0.1:26379");
using var proxy = new Toxiproxy(new Uri("http://127.0.0.1:8474"), "redis", "0.0.0.0:26379", "redis:6379");
try
{
    await proxy.EnsureProxyAsync();
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"toxiproxy is not reachable ({ex.Message}). Run `docker compose up -d` in {AppContext.BaseDirectory}../../../");
    return 1;
}

var report = new StringBuilder();
report.AppendLine("# Redis.Client vs StackExchange.Redis")
    .AppendLine()
    .AppendLine($"Запуск: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}, {Environment.ProcessorCount} ядер, .NET {Environment.Version}, " +
                $"StackExchange.Redis {typeof(StackExchange.Redis.ConnectionMultiplexer).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]}.")
    .AppendLine("Оба клиента: одно мультиплексированное соединение, ConnectTimeout и CommandTimeout одинаковые, остальное по умолчанию.")
    .AppendLine();

var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/comparison"));
Directory.CreateDirectory(outputDirectory);
var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

if (mode is "perf" or "all")
{
    await RunPerfAsync();
}

if (mode is "chaos" or "all")
{
    await RunChaosAsync();
}

var reportPath = Path.Combine(outputDirectory, $"report-{stamp}.md");
await File.WriteAllTextAsync(reportPath, report.ToString());
Console.WriteLine();
Console.WriteLine(report);
Console.WriteLine($"Отчёт: {reportPath}");
return 0;

async Task RunPerfAsync()
{
    var duration = TimeSpan.FromSeconds(quick ? 3 : 10);
    const int Concurrency = 256;
    var settings = (IPEndPoint endpoint) => new ClientSettings(endpoint, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    var networks = new (string Name, IPEndPoint Endpoint, int ToxicLatencyMs)[]
    {
        ("localhost", direct, 0),
        ("RTT +1 мс", proxied, 1),
    };

    report.AppendLine("## Производительность")
        .AppendLine()
        .AppendLine($"Пропускная способность: {Concurrency} параллельных вызывающих в замкнутом цикле, {duration.TotalSeconds:0} с. " +
                    "Задержки: открытый цикл на 50% пропускной способности более медленного клиента, время считается от " +
                    "запланированного момента старта (без coordinated omission). CPU и аллокации на операцию учитывают весь процесс, " +
                    "включая одинаковую для обоих клиентов обвязку.")
        .AppendLine()
        .AppendLine("| Сеть | Команда | Значение | Клиент | оп/с | CPU, мкс/оп | Аллокации, Б/оп | Нагрузка для задержек | p50, мкс | p99, мкс | p99.9, мкс | Ошибки |")
        .AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

    var order = 0;
    foreach (var network in networks)
    {
        if (network.ToxicLatencyMs > 0)
        {
            await proxy.AddToxicAsync("rtt", "latency", new JsonObject { ["latency"] = network.ToxicLatencyMs, ["jitter"] = 0 });
        }

        await using IClientAdapter ours = new RedisClientAdapter(settings(network.Endpoint));
        await using IClientAdapter theirs = new StackExchangeAdapter(settings(network.Endpoint));
        await ours.ConnectAsync();
        await theirs.ConnectAsync();

        foreach (var size in new[] { 100, 16 * 1024 })
        {
            var payload = new byte[size];
            Random.Shared.NextBytes(payload);
            foreach (var operation in new[] { "GET", "SET" })
            {
                // Alternate who goes first so that warm-up and thermal effects do not favour one client.
                IClientAdapter[] pair = order++ % 2 == 0 ? [ours, theirs] : [theirs, ours];
                var saturation = new Dictionary<string, PerfResult>();
                foreach (var client in pair)
                {
                    Console.WriteLine($"perf: {network.Name} {operation} {size} B {client.Name} (насыщение)");
                    saturation[client.Name] = await PerfScenario.SaturateAsync(client, network.Name, operation, payload, Concurrency, duration);
                }

                var rate = (int)(saturation.Values.Min(r => r.OpsPerSecond) / 2);
                foreach (var client in new[] { ours, theirs })
                {
                    Console.WriteLine($"perf: {network.Name} {operation} {size} B {client.Name} ({rate} оп/с)");
                    var fixedRate = await PerfScenario.FixedRateAsync(client, network.Name, operation, payload, rate, duration);
                    var s = saturation[client.Name];
                    report.AppendLine($"| {network.Name} | {operation} | {FormatSize(size)} | {client.Name} | {s.OpsPerSecond:N0} | " +
                                      $"{s.CpuUsPerOp:F1} | {s.AllocatedBytesPerOp:N0} | {rate:N0} оп/с | {fixedRate.P50Us:N0} | " +
                                      $"{fixedRate.P99Us:N0} | {fixedRate.P999Us:N0} | {s.Errors + fixedRate.Errors} |");
                }
            }
        }

        if (network.ToxicLatencyMs > 0)
        {
            await proxy.RemoveToxicAsync("rtt");
        }
    }

    report.AppendLine();
}

async Task RunChaosAsync()
{
    var fault = TimeSpan.FromSeconds(quick ? 5 : 10);
    var calm = TimeSpan.FromSeconds(quick ? 6 : 10);
    const int Workers = 32;
    var settings = new ClientSettings(proxied, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    await proxy.EnsureProxyAsync();
    await using IClientAdapter ours = new RedisClientAdapter(settings);
    await using IClientAdapter theirs = new StackExchangeAdapter(settings);
    await ours.ConnectAsync();
    await theirs.ConnectAsync();

    Console.WriteLine("chaos:");
    var scenario = new ChaosScenario([ours, theirs], proxy, Workers, calm);
    var (outcomes, timeline) = await scenario.RunAsync(
        [.. ChaosScenario.DefaultPhases(fault).Where(p => onlyPhases is null || onlyPhases.Contains(p.Id))], baseline: TimeSpan.FromSeconds(5));
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"chaos-timeline-{stamp}.csv"), timeline);

    report.AppendLine("## Поведение при сбоях")
        .AppendLine()
        .AppendLine($"Оба клиента одновременно через один toxiproxy, по {Workers} потоков: SET уникального значения, затем GET и сверка, " +
                    $"пауза 10 мс. ConnectTimeout = CommandTimeout = 2 с. После устранения каждого сбоя {calm.TotalSeconds:0} с " +
                    "наблюдения. «Восстановление» — сколько секунд после устранения сбоя ещё были ошибки или успешных операций было " +
                    "меньше половины от исходного уровня. «Несовпадения» — GET вернул не то значение, которое этот поток только что записал.")
        .AppendLine()
        .AppendLine("| Сбой | Клиент | Успешно | Ошибки | Несовпадения | Макс. время операции, мс | Восстановление, с |")
        .AppendLine("|---|---|---:|---:|---:|---:|---:|");
    foreach (var o in outcomes.OrderBy(o => o.Phase == outcomes[0].Phase ? 0 : 1).ThenBy(o => IndexOfPhase(o.Phase)).ThenBy(o => o.Client))
    {
        var recovery = o.RecoverySeconds switch
        {
            null => "—",
            double.PositiveInfinity => $"не восстановился за {calm.TotalSeconds:0}",
            var r => $"{r:F1}",
        };
        report.AppendLine($"| {o.Phase} | {o.Client} | {o.Succeeded:N0} | {o.Errors:N0} | {o.Mismatches:N0} | {o.MaxLatencyMs:N0} | {recovery} |");
    }

    report.AppendLine();

    int IndexOfPhase(string phase) => outcomes.Select(o => o.Phase).Distinct().ToList().IndexOf(phase);
}

static string FormatSize(int bytes) => bytes >= 1024 ? $"{bytes / 1024} КБ" : $"{bytes} Б";
