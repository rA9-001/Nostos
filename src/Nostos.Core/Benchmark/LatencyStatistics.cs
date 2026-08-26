namespace Nostos.Core.Benchmark;

/// <summary>
/// What a set of latency samples actually says.
///
/// The mean is deliberately not the headline. A player does not notice the average round trip;
/// they notice the samples that arrived late, because those are the ones that turn into a
/// rubber-band or a shot that did not register. That is the tail and the spread, so that is what
/// gets reported: the median as the typical case, p95 and p99 as the bad case, and jitter as how
/// unsteady the connection is between one packet and the next.
/// </summary>
public sealed record LatencyStatistics
{
    public required int Sent { get; init; }
    public required int Received { get; init; }

    public required double MinMs { get; init; }
    public required double MedianMs { get; init; }
    public required double P95Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double MaxMs { get; init; }

    /// <summary>
    /// Mean absolute difference between consecutive samples.
    ///
    /// This is jitter as RFC 3550 means it, not as "max minus min" means it. It answers "how
    /// different is the next packet from this one", which is the quantity a netcode
    /// interpolation buffer has to absorb. A connection with a steady 60 ms is far more playable
    /// than one that alternates 20 and 60, and only this statistic tells them apart.
    /// </summary>
    public required double JitterMs { get; init; }

    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;

    /// <summary>
    /// Percentile by nearest-rank on a sorted copy. No interpolation.
    ///
    /// Interpolating between two samples invents a round trip that never happened. For a
    /// hundred-odd samples the difference is cosmetic, and reporting only times that were
    /// actually observed is worth more than the third decimal place.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0;

        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    /// <summary>
    /// Builds the statistics from samples in the order they were taken.
    ///
    /// Order matters: jitter is defined on consecutive pairs, so this must not be handed a
    /// sorted list. Failed samples are counted for loss and excluded from the timings -- a
    /// dropped packet has no round trip, and scoring it as zero or as the timeout would both be
    /// lies, in opposite directions.
    /// </summary>
    public static LatencyStatistics From(IReadOnlyList<LatencySample> samples)
    {
        var ok = samples.Where(s => s.Succeeded).Select(s => s.RoundTripMs).ToList();

        if (ok.Count == 0)
        {
            return new LatencyStatistics
            {
                Sent = samples.Count,
                Received = 0,
                MinMs = 0,
                MedianMs = 0,
                P95Ms = 0,
                P99Ms = 0,
                MaxMs = 0,
                JitterMs = 0,
            };
        }

        var sorted = ok.OrderBy(v => v).ToList();

        // Jitter runs over the successful samples in arrival order. A gap where a packet was
        // lost is simply skipped rather than treated as a large swing, because the size of that
        // swing would be an artefact of the timeout, not a measurement.
        var jitter = 0.0;
        for (var i = 1; i < ok.Count; i++)
            jitter += Math.Abs(ok[i] - ok[i - 1]);
        if (ok.Count > 1)
            jitter /= ok.Count - 1;

        return new LatencyStatistics
        {
            Sent = samples.Count,
            Received = ok.Count,
            MinMs = sorted[0],
            MedianMs = Percentile(sorted, 50),
            P95Ms = Percentile(sorted, 95),
            P99Ms = Percentile(sorted, 99),
            MaxMs = sorted[^1],
            JitterMs = jitter,
        };
    }
}

/// <summary>
/// Whether two runs actually differ, or whether the difference is noise.
///
/// This exists because it is the single most common failure of every "benchmark" attached to a
/// tweaking tool. Ten samples before, ten after, the median moves by 3%, and the tool reports an
/// improvement. Network latency varies by more than that between two runs where nothing changed
/// at all, so a tool that does not test for it will report a win for any change, including a
/// change that did nothing, including a change that made things worse.
/// </summary>
public static class LatencyComparison
{
    /// <summary>Number of resamples. Enough for a stable interval, cheap enough to be instant.</summary>
    private const int Resamples = 2000;

    /// <summary>
    /// Bootstrap confidence interval for the change in median, in milliseconds.
    ///
    /// Bootstrapping rather than a t-test because latency is not normally distributed -- it has
    /// a hard floor at the speed of light and a long right tail, which is exactly the shape a
    /// t-test handles worst. Resampling makes no assumption about the shape at all: it asks what
    /// range of answers this data would have given if it had come out slightly differently.
    ///
    /// <paramref name="seed"/> is fixed by callers that need a reproducible answer, which is
    /// every test and, arguably, every report a user might be asked to justify.
    /// </summary>
    public static LatencyDifference Compare(
        IReadOnlyList<double> before, IReadOnlyList<double> after, int seed = 20260826)
    {
        if (before.Count < 2 || after.Count < 2)
        {
            return new LatencyDifference(0, 0, 0, Distinguishable: false,
                "not enough samples to say anything");
        }

        var observed = Median(after) - Median(before);

        var random = new Random(seed);
        var deltas = new double[Resamples];
        for (var i = 0; i < Resamples; i++)
            deltas[i] = Median(Resample(after, random)) - Median(Resample(before, random));

        Array.Sort(deltas);
        var low = LatencyStatistics.Percentile(deltas, 2.5);
        var high = LatencyStatistics.Percentile(deltas, 97.5);

        // The interval spanning zero is the whole point: it means "this data is consistent with
        // the change having done nothing", which is the correct answer far more often than
        // anyone selling a tweak would like.
        var distinguishable = low > 0 || high < 0;

        var summary = distinguishable
            ? $"{(observed < 0 ? "faster" : "slower")} by {Math.Abs(observed):0.00} ms "
              + $"(95% CI {low:0.00} to {high:0.00} ms)"
            : $"no measurable difference (95% CI {low:0.00} to {high:0.00} ms spans zero)";

        return new LatencyDifference(observed, low, high, distinguishable, summary);
    }

    private static double[] Resample(IReadOnlyList<double> source, Random random)
    {
        var drawn = new double[source.Count];
        for (var i = 0; i < drawn.Length; i++)
            drawn[i] = source[random.Next(source.Count)];
        return drawn;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return LatencyStatistics.Percentile(sorted, 50);
    }
}

/// <param name="MedianDeltaMs">Negative is an improvement: the round trip got shorter.</param>
/// <param name="Distinguishable">False when the interval spans zero, i.e. this could be noise.</param>
public sealed record LatencyDifference(
    double MedianDeltaMs,
    double LowMs,
    double HighMs,
    bool Distinguishable,
    string Summary);
