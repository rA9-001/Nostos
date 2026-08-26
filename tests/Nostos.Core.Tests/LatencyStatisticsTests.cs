using Nostos.Core.Benchmark;

namespace Nostos.Core.Tests;

/// <summary>
/// The arithmetic behind the benchmark.
///
/// This is the part that has to be right, because it is the part a user will quote at somebody.
/// A percentile that is off by one sample or a comparison that calls noise an improvement turns
/// the feature from evidence into the same marketing every other tool in this category ships.
/// </summary>
public sealed class LatencyStatisticsTests
{
    private static IReadOnlyList<LatencySample> Ok(params double[] ms)
        => [.. ms.Select(v => new LatencySample(v, true))];

    [Fact]
    public void Percentiles_only_ever_report_times_that_were_actually_observed()
    {
        // Nearest-rank, not interpolated. An interpolated p95 is a round trip that never
        // happened, and this tool does not get to invent measurements.
        var statistics = LatencyStatistics.From(Ok(10, 20, 30, 40, 1000));

        Assert.Equal(30, statistics.MedianMs);
        Assert.Equal(1000, statistics.P99Ms);
        Assert.Equal(10, statistics.MinMs);
        Assert.Equal(1000, statistics.MaxMs);
    }

    [Fact]
    public void Jitter_is_the_change_between_consecutive_packets_not_the_range()
    {
        // Two connections with an identical median, an identical min and an identical max. The
        // first is steady and the second is unplayable, and jitter is the only number here that
        // can tell them apart -- which is the entire reason it is reported.
        var steady = LatencyStatistics.From(Ok(20, 20, 20, 20, 20, 20, 60));
        var alternating = LatencyStatistics.From(Ok(20, 60, 20, 60, 20, 60, 20));

        Assert.Equal(steady.MedianMs, alternating.MedianMs);
        Assert.Equal(steady.MaxMs, alternating.MaxMs);
        Assert.True(alternating.JitterMs > steady.JitterMs * 5);
    }

    [Fact]
    public void A_lost_packet_is_counted_as_loss_and_kept_out_of_the_timings()
    {
        // Scoring a drop as 0 would flatter the median; scoring it as the timeout would wreck
        // it. Both are lies, so it is counted where it belongs and nowhere else.
        var samples = new List<LatencySample>
        {
            new(10, true), new(0, false), new(10, true), new(10, true),
        };

        var statistics = LatencyStatistics.From(samples);

        Assert.Equal(10, statistics.MedianMs);
        Assert.Equal(4, statistics.Sent);
        Assert.Equal(3, statistics.Received);
        Assert.Equal(25, statistics.LossPercent);
    }

    [Fact]
    public void A_run_where_nothing_answered_reports_zeroes_rather_than_throwing()
    {
        var statistics = LatencyStatistics.From([new(0, false), new(0, false)]);

        Assert.Equal(0, statistics.Received);
        Assert.Equal(100, statistics.LossPercent);
        Assert.Equal(0, statistics.MedianMs);
    }

    [Fact]
    public void Two_samples_of_the_same_noisy_connection_are_not_called_a_difference()
    {
        // The failure mode this whole comparison exists to prevent. Both runs are drawn from
        // the same distribution and their medians will differ by a little, as they always do.
        // A tool that reports that as a win reports a win for everything.
        var random = new Random(1);
        var before = Enumerable.Range(0, 120).Select(_ => 20 + random.NextDouble() * 10).ToList();
        var after = Enumerable.Range(0, 120).Select(_ => 20 + random.NextDouble() * 10).ToList();

        var difference = LatencyComparison.Compare(before, after);

        Assert.False(difference.Distinguishable);
        Assert.Contains("no measurable difference", difference.Summary);
    }

    [Fact]
    public void A_real_improvement_is_reported_as_one()
    {
        var random = new Random(2);
        var before = Enumerable.Range(0, 120).Select(_ => 40 + random.NextDouble() * 5).ToList();
        var after = Enumerable.Range(0, 120).Select(_ => 20 + random.NextDouble() * 5).ToList();

        var difference = LatencyComparison.Compare(before, after);

        Assert.True(difference.Distinguishable);
        Assert.True(difference.MedianDeltaMs < 0);
        Assert.Contains("faster", difference.Summary);
    }

    [Fact]
    public void A_regression_is_reported_as_one()
    {
        // Arguably the most valuable direction, and the one no tweaking tool ever reports.
        var random = new Random(3);
        var before = Enumerable.Range(0, 120).Select(_ => 20 + random.NextDouble() * 5).ToList();
        var after = Enumerable.Range(0, 120).Select(_ => 35 + random.NextDouble() * 5).ToList();

        var difference = LatencyComparison.Compare(before, after);

        Assert.True(difference.Distinguishable);
        Assert.True(difference.MedianDeltaMs > 0);
        Assert.Contains("slower", difference.Summary);
    }

    [Fact]
    public void The_same_two_runs_always_produce_the_same_verdict()
    {
        // The bootstrap is randomised, so the seed is fixed. A verdict that changed when you
        // ran the comparison twice would be worth nothing to anybody trying to justify it.
        var random = new Random(4);
        var before = Enumerable.Range(0, 60).Select(_ => 30 + random.NextDouble() * 8).ToList();
        var after = Enumerable.Range(0, 60).Select(_ => 25 + random.NextDouble() * 8).ToList();

        Assert.Equal(
            LatencyComparison.Compare(before, after).Summary,
            LatencyComparison.Compare(before, after).Summary);
    }

    [Fact]
    public void Too_few_samples_says_so_instead_of_guessing()
    {
        var difference = LatencyComparison.Compare([10], [20]);

        Assert.False(difference.Distinguishable);
        Assert.Contains("not enough samples", difference.Summary);
    }
}
