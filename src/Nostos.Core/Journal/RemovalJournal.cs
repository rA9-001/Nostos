using Nostos.Core.Abstractions;

namespace Nostos.Core.Journal;

/// <summary>
/// Records a program being uninstalled, in the same log as everything else.
///
/// Removing a program is the largest thing this app can do to a machine, so it would be strange
/// for it to be the one thing the History tab did not know about. What the line answers, months
/// later, is "what happened to that?" -- and, because the sweep names its own counts, "did
/// Nostos take anything else with it".
///
/// <para><b>Committed, with no preceding intent</b>, exactly like a startup switch and for the
/// same reason: the outstanding set that `nos revert --all` works from is built out of intents
/// carrying a snapshot, so a committed-only line is visible in the history and is never
/// something revert goes looking for. Reverting an unrelated tweak must not attempt to
/// reinstall Discord.</para>
///
/// <para>That is not the same as saying the removal cannot be undone. Folders went to the
/// Recycle Bin and registry keys were exported to .reg files first, so the undo exists -- it is
/// just performed by Windows and by `reg import`, in the places people already look, rather than
/// by a tweak engine that has no way to reinstall a program.</para>
/// </summary>
public static class RemovalJournal
{
    /// <summary>The prefix that marks a journal line as an app removal.</summary>
    public const string IdPrefix = "app:";

    /// <summary>The origin tag these lines carry.</summary>
    public const string Origin = "uninstall";

    /// <summary>True when a journal line describes a program being removed.</summary>
    public static bool Owns(string tweakId)
        => tweakId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The program's name, recovered from a line this wrote. Falls back to the raw id.</summary>
    public static string NameOf(string tweakId, string? detail)
    {
        if (detail is { Length: > 0 } text)
        {
            var dash = text.IndexOf('—');
            return dash > 0 ? text[..dash].Trim() : text.Trim();
        }

        return Owns(tweakId) ? tweakId[IdPrefix.Length..] : tweakId;
    }

    /// <summary>
    /// The detail line: what was removed and what the sweep did.
    ///
    /// Built here rather than at the call site so the app, the CLI and the service cannot drift
    /// into three different phrasings of the same event.
    /// </summary>
    public static string Describe(string name, int foldersRecycled, int keysRemoved)
    {
        if (foldersRecycled == 0 && keysRemoved == 0)
            return $"{name} — uninstalled, nothing left behind";

        var parts = new List<string>(2);
        if (foldersRecycled > 0)
            parts.Add($"{foldersRecycled} folder(s) to the Recycle Bin");
        if (keysRemoved > 0)
            parts.Add($"{keysRemoved} registry key(s) backed up and removed");

        return $"{name} — uninstalled, {string.Join(", ", parts)}";
    }

    public static Task RecordAsync(
        IJournal journal,
        string appId,
        string name,
        int foldersRecycled,
        int keysRemoved,
        CancellationToken ct = default)
        => journal.AppendAsync(
            new JournalEntry
            {
                EntryId = Guid.NewGuid().ToString("n"),
                TimestampUtc = DateTimeOffset.UtcNow,
                TweakId = IdPrefix + appId,
                Action = JournalAction.ApplyCommitted,
                Origin = Origin,
                Detail = Describe(name, foldersRecycled, keysRemoved),
            },
            ct);
}
