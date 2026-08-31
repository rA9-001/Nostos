using Nostos.Core.Abstractions;

namespace Nostos.Core.Journal;

/// <summary>
/// Records a startup entry being switched, in the same log as everything else.
///
/// Three places do the switching -- the service for machine-wide entries, the app for per-user
/// ones, and the CLI -- and all three have to write the same shape of line, or the History tab
/// tells a different story depending on where the click happened.
///
/// **The entry is deliberately <see cref="JournalAction.ApplyCommitted"/> with no preceding
/// intent.** That is what keeps it out of `nos revert --all`: the outstanding set is built from
/// intents that carry a snapshot, so a committed-only line is visible in the history and is
/// never something revert goes looking for. The alternative was not recording it at all, which
/// is what this replaced -- but a revert that silently turns Razer Synapse back on months later,
/// as a side effect of undoing something unrelated, is worse than either.
///
/// Nothing is lost by that. Undoing a startup switch is one click in the tab that did it, and
/// unlike a registry value there is no prior value to restore: the state is on or off, and it is
/// visible in Task Manager as well.
/// </summary>
public static class StartupJournal
{
    /// <summary>The prefix that marks a journal line as a startup switch rather than a tweak.</summary>
    public const string IdPrefix = "startup:";

    /// <summary>The origin tag these lines carry.</summary>
    public const string Origin = "startup";

    /// <summary>True when a journal line describes a startup switch.</summary>
    public static bool Owns(string tweakId)
        => tweakId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The program's name, recovered from a line this wrote. Falls back to the raw id.</summary>
    public static string NameOf(string tweakId, string? detail)
    {
        // The name is carried in the detail rather than parsed back out of the id, because an
        // id is a location -- "user-run:Steam" -- and the reader wants the program.
        if (detail is { Length: > 0 } text)
        {
            var arrow = text.IndexOf('→');
            if (arrow > 0)
                return text[..arrow].Trim();
        }

        return Owns(tweakId) ? tweakId[IdPrefix.Length..] : tweakId;
    }

    /// <summary>True when the line records an entry being switched on rather than off.</summary>
    public static bool WasEnabled(string? detail)
        => detail is not null && detail.EndsWith("on", StringComparison.OrdinalIgnoreCase);

    public static Task RecordAsync(
        IJournal journal,
        string entryId,
        string name,
        bool enabled,
        CancellationToken ct = default)
        => journal.AppendAsync(
            new JournalEntry
            {
                EntryId = Guid.NewGuid().ToString("n"),
                TimestampUtc = DateTimeOffset.UtcNow,
                TweakId = IdPrefix + entryId,
                Action = JournalAction.ApplyCommitted,
                Origin = Origin,
                Detail = $"{name} → {(enabled ? "on" : "off")}",
            },
            ct);
}
