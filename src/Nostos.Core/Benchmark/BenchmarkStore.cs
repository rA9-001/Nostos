using System.Text.Json;

namespace Nostos.Core.Benchmark;

/// <summary>
/// One completed measurement, and what the machine looked like when it was taken.
///
/// <see cref="AppliedTweaks"/> is the field that makes the whole feature worth having. A latency
/// number on its own is a number; a latency number next to the list of tweaks that were applied
/// when it was taken is evidence. Without it, comparing two runs a week apart means trusting
/// your memory of what you had changed in between, which is exactly the thing nobody in this
/// hobby is good at.
/// </summary>
public sealed record BenchmarkRun
{
    public required string Id { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }

    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Kind { get; init; }

    /// <summary>Free-text note, e.g. "before" or "after interrupt moderation".</summary>
    public string Label { get; init => field = value ?? ""; } = "";

    /// <summary>Ids this program had applied when the run was taken, from the journal.</summary>
    public IReadOnlyList<string> AppliedTweaks { get; init => field = value ?? []; } = [];

    /// <summary>
    /// Every successful round trip, in the order taken.
    ///
    /// Kept in full rather than summarised. A comparison between two runs has to resample the
    /// originals to say whether a difference is real, and percentiles cannot be resampled -- so
    /// a store that keeps only the summary can never answer the only question worth asking.
    /// Two hundred doubles is 1.6 KB.
    /// </summary>
    public required IReadOnlyList<double> SamplesMs { get; init; }

    public required int Sent { get; init; }

    public LatencyStatistics Statistics => field ??= LatencyStatistics.From(
        [.. SamplesMs.Select(ms => new LatencySample(ms, true)),
         .. Enumerable.Repeat(new LatencySample(0, false), Math.Max(0, Sent - SamplesMs.Count))]);
}

/// <summary>
/// Append-only history of measurements, one JSON object per line.
///
/// The same format and the same reasoning as the change journal next to it: a line is written
/// once and never edited, so a crash can lose the last line and nothing else. Runs are
/// deliberately never pruned by this program -- a baseline taken six months ago is the most
/// valuable row in the file, and a tool that tidied it away would be deleting the only evidence
/// the user has.
/// </summary>
public sealed class BenchmarkStore
{
    private readonly string _path;

    public BenchmarkStore(string? path = null) => _path = path ?? AppPaths.BenchmarkPath;

    public string Path => _path;

    public async Task AppendAsync(BenchmarkRun run, CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var line = JsonSerializer.Serialize(run, BenchmarkJson.Default.BenchmarkRun);
        await File.AppendAllTextAsync(_path, line + Environment.NewLine, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BenchmarkRun>> ReadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return [];

        var runs = new List<BenchmarkRun>();
        foreach (var line in await File.ReadAllLinesAsync(_path, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                if (JsonSerializer.Deserialize(line, BenchmarkJson.Default.BenchmarkRun) is { } run)
                    runs.Add(run);
            }
            catch (JsonException)
            {
                // A torn final line from a power cut mid-append. Skipping it keeps every
                // complete run readable, which is the point of a line-per-record format.
            }
        }

        return runs;
    }
}
