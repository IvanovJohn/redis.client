using System.Diagnostics;

namespace Redis.Client.Comparison;

internal sealed record PerfResult(
    string Client,
    string Network,
    string Operation,
    int PayloadBytes,
    string Mode,
    double OpsPerSecond,
    double P50Us,
    double P99Us,
    double P999Us,
    double CpuUsPerOp,
    double AllocatedBytesPerOp,
    long Errors);

/// <summary>
/// Two measurements per configuration:
/// <list type="bullet">
/// <item>saturation: a fixed number of callers in a closed loop, reporting throughput;</item>
/// <item>fixed rate: an open loop at a given request rate, latency measured from the <i>intended</i> start time
/// so that a stalled client is not flattered by issuing fewer requests (coordinated omission).</item>
/// </list>
/// CPU time and allocations are process-wide deltas. Allocations are reported for the closed loop only: the open loop
/// allocates a task per request in the harness itself.
/// </summary>
internal static class PerfScenario
{
    public static async Task<PerfResult> SaturateAsync(
        IClientAdapter client, string network, string operation, byte[] payload, int concurrency, TimeSpan duration)
    {
        var key = $"cmp:perf:{client.Name}";
        await client.SetAsync(key, payload);
        var op = Operation(client, operation, key, payload);
        await RunClosedLoopAsync(op, concurrency, TimeSpan.FromSeconds(1)); // warm-up

        var measure = Measurement.Start();
        var (count, errors) = await RunClosedLoopAsync(op, concurrency, duration);
        var (cpu, allocated) = measure.Stop();
        return new PerfResult(client.Name, network, operation, payload.Length, $"насыщение, {concurrency} вызывающих",
            count / duration.TotalSeconds, double.NaN, double.NaN, double.NaN, cpu / count, (double)allocated / count, errors);
    }

    public static async Task<PerfResult> FixedRateAsync(
        IClientAdapter client, string network, string operation, byte[] payload, int rate, TimeSpan duration)
    {
        var key = $"cmp:perf:{client.Name}";
        await client.SetAsync(key, payload);
        var op = Operation(client, operation, key, payload);
        await RunOpenLoopAsync(op, rate, TimeSpan.FromSeconds(1)); // warm-up

        var measure = Measurement.Start();
        var (latencies, errors) = await RunOpenLoopAsync(op, rate, duration);
        var (cpu, _) = measure.Stop();
        Array.Sort(latencies);
        return new PerfResult(client.Name, network, operation, payload.Length, $"фикс. нагрузка, {rate:N0} оп/с",
            latencies.Length / duration.TotalSeconds, Percentile(latencies, 0.50), Percentile(latencies, 0.99),
            Percentile(latencies, 0.999), cpu / latencies.Length, double.NaN, errors);
    }

    private static Func<ValueTask> Operation(IClientAdapter client, string operation, string key, byte[] payload) =>
        operation switch
        {
            "GET" => async () => await client.GetLengthAsync(key),
            "SET" => () => client.SetAsync(key, payload),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private static async Task<(long Count, long Errors)> RunClosedLoopAsync(Func<ValueTask> op, int concurrency, TimeSpan duration)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        long count = 0;
        long errors = 0;
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
        {
            long completed = 0;
            while (Stopwatch.GetTimestamp() < deadline)
            {
                try
                {
                    await op();
                    completed++;
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref errors);
                }
            }

            Interlocked.Add(ref count, completed);
        }));
        return (count, errors);
    }

    private static async Task<(long[] Latencies, long Errors)> RunOpenLoopAsync(Func<ValueTask> op, int rate, TimeSpan duration)
    {
        var total = (int)(rate * duration.TotalSeconds);
        var interval = (double)Stopwatch.Frequency / rate;
        var latencies = new long[total];
        long errors = 0;
        var remaining = total;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Timers are far too coarse for this (Task.Delay(1) sleeps several ms under WSL), so a dedicated thread spins
        // until each request is due. It costs one core, which is why CPU per op is taken from the closed loop only.
        var pacer = new Thread(() =>
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < total; i++)
            {
                var intended = start + (long)(i * interval);
                while (Stopwatch.GetTimestamp() < intended)
                {
                    Thread.SpinWait(20);
                }

                _ = Issue(i, intended);
            }
        })
        { IsBackground = true, Name = "open-loop pacer" };
        pacer.Start();
        await done.Task;
        return ([.. latencies.Where(x => x >= 0)], errors);

        async Task Issue(int index, long intended)
        {
            try
            {
                await op();
                latencies[index] = Stopwatch.GetTimestamp() - intended;
            }
            catch (Exception)
            {
                latencies[index] = -1;
                Interlocked.Increment(ref errors);
            }

            if (Interlocked.Decrement(ref remaining) == 0)
            {
                done.TrySetResult();
            }
        }
    }

    private static double Percentile(long[] sorted, double p) =>
        sorted.Length == 0 ? double.NaN : sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))] * 1_000_000.0 / Stopwatch.Frequency;

    private readonly record struct Measurement(TimeSpan Cpu, long Allocated)
    {
        public static Measurement Start()
        {
            GC.Collect();
            return new Measurement(Process.GetCurrentProcess().TotalProcessorTime, GC.GetTotalAllocatedBytes(precise: true));
        }

        public (double CpuUs, long Allocated) Stop() =>
            ((Process.GetCurrentProcess().TotalProcessorTime - Cpu).TotalMicroseconds, GC.GetTotalAllocatedBytes(precise: true) - Allocated);
    }
}
