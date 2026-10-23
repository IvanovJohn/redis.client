using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Redis.Client.Comparison;

internal sealed record ChaosPhase(string Id, string Name, TimeSpan FaultDuration, Func<Toxiproxy, Task> Inject, Func<Toxiproxy, Task> Heal);

internal sealed record PhaseOutcome(
    string Phase,
    string Client,
    long Succeeded,
    long Errors,
    long Mismatches,
    double MaxLatencyMs,
    double? RecoverySeconds);

/// <summary>
/// Both clients run the same verified workload at the same time through one toxiproxy, so every fault hits them
/// identically. Each worker writes a unique value and reads it back: a read that returns anything else is a
/// mismatch (a reply delivered to the wrong caller, or a timed-out write applied after a later one).
/// </summary>
internal sealed class ChaosScenario(IReadOnlyList<IClientAdapter> clients, Toxiproxy proxy, int workersPerClient, TimeSpan calm)
{
    private const int BucketMs = 100;

    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly List<(string Name, double FaultAt, double HealedAt, double EndAt)> _phases = [];
    private Stats[] _stats = [];

    public static IReadOnlyList<ChaosPhase> DefaultPhases(TimeSpan fault) =>
    [
        new("rst", "Обрыв соединений (TCP RST)", TimeSpan.FromSeconds(1),
            p => p.AddToxicAsync("rst", "reset_peer", new JsonObject { ["timeout"] = 0 }),
            p => p.RemoveToxicAsync("rst")),
        new("down", $"Redis недоступен {fault.TotalSeconds:0} с", fault,
            p => p.SetEnabledAsync(false),
            p => p.SetEnabledAsync(true)),
        new("freeze", $"Сеть замерла на {fault.TotalSeconds:0} с (ответы не приходят, соединение не рвётся)", fault,
            p => p.AddToxicAsync("blackhole", "timeout", new JsonObject { ["timeout"] = 0 }),
            p => p.RemoveToxicAsync("blackhole")),
        new("slow", $"Задержка ответов +3 с в течение {fault.TotalSeconds:0} с", fault,
            p => p.AddToxicAsync("slow", "latency", new JsonObject { ["latency"] = 3000, ["jitter"] = 0 }),
            p => p.RemoveToxicAsync("slow")),
        new("slicer", $"Ответы приходят по 1 байту {fault.TotalSeconds:0} с", fault,
            p => p.AddToxicAsync("slicer", "slicer", new JsonObject { ["average_size"] = 1, ["size_variation"] = 0, ["delay"] = 0 }),
            p => p.RemoveToxicAsync("slicer")),
        new("restart", "Перезапуск Redis", TimeSpan.Zero,
            _ => Toxiproxy.DockerAsync("restart", "cmp-redis"),
            _ => Task.CompletedTask),
    ];

    private double Now => Stopwatch.GetElapsedTime(_start).TotalSeconds;

    public async Task<(IReadOnlyList<PhaseOutcome> Outcomes, string TimelineCsv)> RunAsync(IReadOnlyList<ChaosPhase> phases, TimeSpan baseline)
    {
        var total = baseline + phases.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.FaultDuration + calm) + TimeSpan.FromMinutes(1);
        _stats = [.. clients.Select(c => new Stats(c.Name, (int)(total.TotalMilliseconds / BucketMs)))];

        using var stop = new CancellationTokenSource();
        var workers = clients.SelectMany((client, index) =>
            Enumerable.Range(0, workersPerClient).Select(worker => Task.Run(() => WorkerAsync(client, _stats[index], worker, stop.Token)))).ToArray();

        await Task.Delay(baseline);
        _phases.Add(("Исходное состояние", 0, 0, Now));
        foreach (var phase in phases)
        {
            Console.WriteLine($"  [{Now,6:F1} с] {phase.Name}");
            var faultAt = Now;
            await phase.Inject(proxy);
            await Task.Delay(phase.FaultDuration);
            await phase.Heal(proxy);
            var healedAt = Now;
            await Task.Delay(calm);
            _phases.Add((phase.Name, faultAt, healedAt, Now));
        }

        await stop.CancelAsync();
        await Task.WhenAll(workers);
        return (Summarize(), Timeline());
    }

    private async Task WorkerAsync(IClientAdapter client, Stats stats, int worker, CancellationToken stop)
    {
        var key = $"cmp:chaos:{client.Name}:{worker}";
        var seq = 0L;
        while (!stop.IsCancellationRequested)
        {
            var value = Encoding.UTF8.GetBytes($"{worker}:{++seq}");
            var started = Stopwatch.GetTimestamp();
            Outcome outcome;
            try
            {
                await client.SetAsync(key, value);
                outcome = await client.GetMatchesAsync(key, value) ? Outcome.Ok : Outcome.Mismatch;
            }
            catch (Exception)
            {
                outcome = Outcome.Error;
            }

            stats.Record(Bucket(Now), outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            try
            {
                await Task.Delay(10, stop);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static int Bucket(double seconds) => (int)(seconds * 1000 / BucketMs);

    private List<PhaseOutcome> Summarize()
    {
        var result = new List<PhaseOutcome>();
        var baselineEnd = Bucket(_phases[0].EndAt);
        foreach (var stats in _stats)
        {
            // Healthy = no errors and at least half of the baseline success rate.
            var baselineRate = Enumerable.Range(1, baselineEnd - 1).Average(i => stats.Ok[i]);
            bool Healthy(int i) => stats.Errors[i] == 0 && stats.Ok[i] >= baselineRate / 2;

            foreach (var (name, faultAt, healedAt, endAt) in _phases)
            {
                int from = Bucket(faultAt), healed = Bucket(healedAt), to = Bucket(endAt);
                double? recovery = null;
                if (name != _phases[0].Name)
                {
                    var lastBad = Enumerable.Range(healed, to - healed).LastOrDefault(i => !Healthy(i), healed - 1);
                    recovery = lastBad >= to - 2 ? double.PositiveInfinity : Math.Max(0, (lastBad + 1 - healed) * BucketMs / 1000.0);
                }

                var range = Enumerable.Range(from, to - from);
                result.Add(new PhaseOutcome(name, stats.Name,
                    range.Sum(i => stats.Ok[i]), range.Sum(i => stats.Errors[i]), range.Sum(i => stats.Mismatches[i]),
                    range.Max(i => stats.MaxLatencyMs[i]), recovery));
            }
        }

        return result;
    }

    private string Timeline()
    {
        var csv = new StringBuilder("seconds,phase,client,ok,errors,mismatches,max_latency_ms\n");
        var end = Bucket(_phases[^1].EndAt);
        for (var i = 0; i < end; i++)
        {
            var at = i * BucketMs / 1000.0;
            var phase = _phases.LastOrDefault(p => p.FaultAt <= at && p.FaultAt > 0).Name ?? _phases[0].Name;
            foreach (var stats in _stats)
            {
                csv.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{at:F1},\"{phase}\",{stats.Name},{stats.Ok[i]},{stats.Errors[i]},{stats.Mismatches[i]},{stats.MaxLatencyMs[i]:F0}\n");
            }
        }

        return csv.ToString();
    }

    private enum Outcome
    {
        Ok,
        Error,
        Mismatch,
    }

    private sealed class Stats(string name, int buckets)
    {
        public string Name { get; } = name;

        public long[] Ok { get; } = new long[buckets];

        public long[] Errors { get; } = new long[buckets];

        public long[] Mismatches { get; } = new long[buckets];

        public double[] MaxLatencyMs { get; } = new double[buckets];

        public void Record(int bucket, Outcome outcome, double latencyMs)
        {
            if (bucket >= Ok.Length)
            {
                return;
            }

            Interlocked.Increment(ref (outcome switch
            {
                Outcome.Ok => Ok,
                Outcome.Error => Errors,
                _ => Mismatches,
            })[bucket]);
            lock (MaxLatencyMs)
            {
                MaxLatencyMs[bucket] = Math.Max(MaxLatencyMs[bucket], latencyMs);
            }
        }
    }
}
