using Nostos.Core.Benchmark;

namespace Nostos.Cli.Commands;

/// <summary>
/// Measures network latency, and says honestly whether two measurements differ.
///
/// The reason this is in the tool at all is not to prove that a tweak worked. It is to catch the
/// two outcomes nobody else reports: the change that did nothing, and the change that made
/// things worse. Both are common, and both are invisible if you only ever look at a number after
/// the fact and compare it against a memory.
///
/// What it cannot do is settle most of the catalog. The adapter tweaks under Ping move
/// microseconds on the local machine; the internet leg of a real connection is milliseconds and
/// varies by more than the whole effect between one minute and the next. That is stated in the
/// output rather than left for the user to discover, because a benchmark that quietly implies
/// more precision than it has is worse than no benchmark.
/// </summary>
public static class BenchCommands
{
    /// <summary>
    /// Cloudflare's resolver, on the DNS port.
    ///
    /// Chosen for the properties a default needs rather than for speed: it is anycast so it is
    /// near almost everybody, it is not a CDN edge that might be inside your ISP, and something
    /// is genuinely listening on 853 so a TCP handshake completes rather than being refused.
    /// Any game server you actually play on is a better target, and `--host` takes one.
    /// </summary>
    private const string DefaultHost = "1.1.1.1";
    private const int DefaultPort = 853;
    private const int DefaultSamples = 120;

    public static async Task<int> RunAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var sub = commandLine.Positional.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "run";

        return sub switch
        {
            "run" => await MeasureAsync(host, commandLine, ct).ConfigureAwait(false),
            "list" => await ListAsync(host, ct).ConfigureAwait(false),
            "compare" => await CompareAsync(host, commandLine, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: nos bench [run] [--host H] [--port N] [--samples N] [--icmp] [--label TEXT]");
        Console.Error.WriteLine("       nos bench list");
        Console.Error.WriteLine("       nos bench compare [<before-id> <after-id>]");
        return 2;
    }

    private static async Task<int> MeasureAsync(CliHost cli, CommandLine commandLine, CancellationToken ct)
    {
        var target = commandLine.Get("host") ?? DefaultHost;
        var port = commandLine.GetInt("port") ?? DefaultPort;
        var samples = Math.Clamp(commandLine.GetInt("samples") ?? DefaultSamples, 10, 5000);
        var kind = commandLine.Has("icmp") ? ProbeKind.Icmp : ProbeKind.TcpConnect;
        var label = commandLine.Get("label") ?? "";

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}Measuring{Ansi.Reset} {samples} samples to {target}" +
                          $"{(kind == ProbeKind.TcpConnect ? $":{port} (TCP handshake)" : " (ICMP echo)")}");
        Console.WriteLine($"{Ansi.Dim}Close anything using the network first. A download in another " +
                          $"window changes this far more than any tweak does.{Ansi.Reset}");
        Console.WriteLine();

        var done = 0;
        var progress = new Progress<LatencySample>(_ =>
        {
            done++;
            if (done % 10 == 0)
                Console.Write($"\r  {done}/{samples}");
        });

        var taken = await new LatencyProbe()
            .RunAsync(target, port, kind, samples, progress: progress, ct: ct)
            .ConfigureAwait(false);

        Console.Write($"\r{new string(' ', 20)}\r");

        var statistics = LatencyStatistics.From(taken);
        Report(statistics);

        // The applied set comes from the journal, not from a re-read of the machine: the
        // question a comparison needs answered is "what had this program done at that moment",
        // and only the journal knows that.
        var outstanding = await cli.Journal.GetOutstandingAsync(ct).ConfigureAwait(false);

        var run = new BenchmarkRun
        {
            Id = Guid.NewGuid().ToString("n")[..8],
            TimestampUtc = DateTimeOffset.UtcNow,
            Host = target,
            Port = port,
            Kind = kind.ToString(),
            Label = label,
            AppliedTweaks = [.. outstanding.Keys.Order(StringComparer.Ordinal)],
            SamplesMs = [.. taken.Where(s => s.Succeeded).Select(s => s.RoundTripMs)],
            Sent = taken.Count,
        };

        var store = new BenchmarkStore();
        await store.AppendAsync(run, ct).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Dim}Saved as {Ansi.Reset}{run.Id}{Ansi.Dim}, with the " +
                          $"{run.AppliedTweaks.Count} tweak(s) applied at the time.{Ansi.Reset}");
        Console.WriteLine($"{Ansi.Dim}Compare two runs with `nos bench compare`.{Ansi.Reset}");
        Console.WriteLine();

        return 0;
    }

    private static void Report(LatencyStatistics statistics)
    {
        Console.WriteLine($"{Ansi.Bold}Round trip{Ansi.Reset}");
        Line("median", $"{statistics.MedianMs:0.00} ms", "the typical case");
        Line("p95", $"{statistics.P95Ms:0.00} ms", "1 packet in 20 was at least this late");
        Line("p99", $"{statistics.P99Ms:0.00} ms", "1 in 100 - this is what you feel");
        Line("min / max", $"{statistics.MinMs:0.00} / {statistics.MaxMs:0.00} ms", "");
        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}Steadiness{Ansi.Reset}");
        Line("jitter", $"{statistics.JitterMs:0.00} ms", "mean change between consecutive packets");

        var lossColour = statistics.LossPercent > 0 ? Ansi.Yellow : Ansi.Dim;
        Line("loss", $"{lossColour}{statistics.LossPercent:0.0}%{Ansi.Reset}",
            $"{statistics.Received} of {statistics.Sent} answered");
    }

    private static void Line(string label, string value, string note)
        => Console.WriteLine($"  {label,-12} {value,-22} {Ansi.Dim}{note}{Ansi.Reset}");

    private static async Task<int> ListAsync(CliHost cli, CancellationToken ct)
    {
        var runs = await new BenchmarkStore().ReadAllAsync(ct).ConfigureAwait(false);

        Console.WriteLine();
        if (runs.Count == 0)
        {
            Console.WriteLine("No measurements yet. Run `nos bench` to take one.");
            Console.WriteLine();
            return 0;
        }

        Console.WriteLine($"{Ansi.Bold}{"id",-10}{"when",-22}{"median",-10}{"p99",-10}{"jitter",-10}{"tweaks",-8}label{Ansi.Reset}");
        foreach (var run in runs)
        {
            var s = run.Statistics;
            Console.WriteLine(
                $"{run.Id,-10}{run.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  " +
                $"{s.MedianMs,-10:0.00}{s.P99Ms,-10:0.00}{s.JitterMs,-10:0.00}" +
                $"{run.AppliedTweaks.Count,-8}{run.Label}");
        }

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Dim}{new BenchmarkStore().Path}{Ansi.Reset}");
        Console.WriteLine();
        return 0;
    }

    private static async Task<int> CompareAsync(CliHost cli, CommandLine commandLine, CancellationToken ct)
    {
        var runs = await new BenchmarkStore().ReadAllAsync(ct).ConfigureAwait(false);
        if (runs.Count < 2)
        {
            Console.Error.WriteLine("Need at least two measurements. Run `nos bench` before and after a change.");
            return 2;
        }

        var ids = commandLine.Positional.Skip(2).ToList();

        // With no arguments, compare the last two. That is what somebody who just ran a before
        // and an after wants, and making them copy two ids to get it would be silly.
        var before = ids.Count >= 2 ? Find(runs, ids[0]) : runs[^2];
        var after = ids.Count >= 2 ? Find(runs, ids[1]) : runs[^1];

        if (before is null || after is null)
        {
            Console.Error.WriteLine("No run with that id. `nos bench list` shows them.");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}Before{Ansi.Reset}  {before.Id}  {before.TimestampUtc.ToLocalTime():g}  " +
                          $"median {before.Statistics.MedianMs:0.00} ms  {before.Label}");
        Console.WriteLine($"{Ansi.Bold}After {Ansi.Reset}  {after.Id}  {after.TimestampUtc.ToLocalTime():g}  " +
                          $"median {after.Statistics.MedianMs:0.00} ms  {after.Label}");

        if (!string.Equals(before.Host, after.Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.Kind, after.Kind, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Yellow}These two measured different things " +
                              $"({before.Kind} to {before.Host} versus {after.Kind} to {after.Host}). " +
                              $"The comparison below is meaningless.{Ansi.Reset}");
        }

        var difference = LatencyComparison.Compare(before.SamplesMs, after.SamplesMs);

        Console.WriteLine();
        var colour = !difference.Distinguishable ? Ansi.Dim
            : difference.MedianDeltaMs < 0 ? Ansi.Green
            : Ansi.Red;
        Console.WriteLine($"{Ansi.Bold}Verdict{Ansi.Reset}  {colour}{difference.Summary}{Ansi.Reset}");

        if (!difference.Distinguishable)
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Dim}That is the most common answer, and it is a real answer. " +
                              $"It does not mean the tweak did nothing to the machine - it means " +
                              $"this measurement cannot tell, because the change is smaller than " +
                              $"the variation between two runs of the same test.{Ansi.Reset}");
        }

        var added = after.AppliedTweaks.Except(before.AppliedTweaks, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = before.AppliedTweaks.Except(after.AppliedTweaks, StringComparer.OrdinalIgnoreCase).ToList();

        Console.WriteLine();
        if (added.Count == 0 && removed.Count == 0)
        {
            Console.WriteLine($"{Ansi.Yellow}Nothing this program changed differs between the two runs.{Ansi.Reset}");
            Console.WriteLine($"{Ansi.Dim}So whatever moved, it was not a tweak from this catalog. " +
                              $"Two runs with an identical machine state is also the right way to " +
                              $"find out how noisy your connection is.{Ansi.Reset}");
        }
        else
        {
            Console.WriteLine($"{Ansi.Bold}Changed between the runs{Ansi.Reset}");
            foreach (var id in added)
                Console.WriteLine($"  {Ansi.Green}+{Ansi.Reset} {id}");
            foreach (var id in removed)
                Console.WriteLine($"  {Ansi.Red}-{Ansi.Reset} {id}");
        }

        Console.WriteLine();
        return 0;
    }

    private static BenchmarkRun? Find(IReadOnlyList<BenchmarkRun> runs, string id)
        => runs.LastOrDefault(r => r.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
}
