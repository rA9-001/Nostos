using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core.Abstractions;
using Nostos.Core.Json;

namespace Nostos.Core.Journal;

/// <summary>
/// Append-only JSON Lines journal.
///
/// Format choice is deliberate: a corrupt or partially written line can be skipped without
/// losing the other 400 entries, which a single top-level JSON array could not survive.
/// The file is meant to be readable with `type journal.jsonl` when someone is troubleshooting
/// a machine that will not boot into the UI.
/// </summary>
public sealed class JsonlJournal : IJournal
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogSink _log;

    public string Path { get; }

    public JsonlJournal(string path, ILogSink? log = null)
    {
        Path = path;
        _log = log ?? NullLogSink.Instance;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task AppendAsync(JournalEntry entry, CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(entry, JournalJsonContext.Default.JournalEntry);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // FileShare.Read so the user can tail the journal while the service is running.
            await using var stream = new FileStream(
                Path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<JournalEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path))
            return [];

        var entries = new List<JournalEntry>();
        var lineNumber = 0;
        await using var stream = new FileStream(
            Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonSerializer.Deserialize(line, JournalJsonContext.Default.JournalEntry) is { } entry)
                    entries.Add(entry);
            }
            catch (JsonException e)
            {
                // A torn final line after a hard power loss is expected. Skip it loudly and keep
                // the rest of the log usable, because the rest of the log is how we un-break the machine.
                _log.Warn($"journal: skipping unreadable line {lineNumber} in {Path}: {e.Message}");
            }
        }

        return entries;
    }

    public async Task<IReadOnlyDictionary<string, TweakSnapshot>> GetOutstandingAsync(CancellationToken ct = default)
    {
        var outstanding = new Dictionary<string, TweakSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in await ReadAllAsync(ct).ConfigureAwait(false))
        {
            switch (entry.Action)
            {
                case JournalAction.ApplyIntent when entry.Snapshot is not null:
                    // Keep the OLDEST snapshot: applying twice in a row must still revert to the
                    // value the machine had before this program ever touched it.
                    outstanding.TryAdd(entry.TweakId, entry.Snapshot);
                    break;

                case JournalAction.RevertCommitted:
                    outstanding.Remove(entry.TweakId);
                    break;

                // ApplyCommitted / ApplyFailed / RevertFailed all leave the tweak outstanding.
                // A failed apply may have changed something before it threw, and a failed revert
                // certainly has not finished undoing it.
            }
        }
        return outstanding;
    }
}
