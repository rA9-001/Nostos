using Nostos.Ipc;

namespace Nostos.App.ViewModels;

/// <summary>
/// One line of the change log, written as a sentence.
///
/// The journal on disk is a machine record: an action enum, a tweak id, an origin tag and a
/// registry fragment. That is the right shape for the thing `nos revert --all` reads, and the
/// wrong shape for somebody trying to answer "what did this program do to my computer".
/// `ApplyCommitted / mmcss.system-responsiveness / gui` is not an answer to that question.
///
/// So this translates. The technical text is kept underneath, because a tool whose whole claim
/// is that it can prove what it changed does not get to hide the proof -- but it is the second
/// line, not the first.
/// </summary>
public sealed class JournalEntryViewModel
{
    private JournalEntryViewModel(JournalLine line, string title)
    {
        Line = line;
        TweakTitle = title;
    }

    public JournalLine Line { get; }

    /// <summary>The tweak's human title, falling back to its id when it is no longer in the catalog.</summary>
    public string TweakTitle { get; }

    public string? Detail => Line.Detail;
    public string? Error => Line.Error;

    /// <summary>Set on the first entry of each day, so the list can print a date header.</summary>
    public string? DayHeader { get; private set; }

    public bool HasDayHeader => DayHeader is not null;

    public bool IsFailure => Line.Action
        is nameof(Core.Journal.JournalAction.ApplyFailed)
        or nameof(Core.Journal.JournalAction.RevertFailed);

    /// <summary>True for a change that was started and never confirmed finished.</summary>
    public bool IsUnfinished { get; private init; }

    /// <summary>Local clock time, e.g. "21:50".</summary>
    public string Time => Line.TimestampUtc.ToLocalTime().ToString("HH:mm");

    /// <summary>
    /// What happened, as a verb and the tweak's own name.
    ///
    /// The verb is kept in front rather than wrapped around the title, because the titles are
    /// already imperative: "Turned on Turn off mouse acceleration" is what the obvious phrasing
    /// produces. Same vocabulary as the activity panel, so the two describe the same event in
    /// the same words.
    /// </summary>
    public string Headline => IsUnfinished
        ? $"Interrupted — {TweakTitle}"
        : Line.Action switch
        {
            nameof(Core.Journal.JournalAction.ApplyCommitted) => $"Applied — {TweakTitle}",
            nameof(Core.Journal.JournalAction.RevertCommitted) => $"Undone — {TweakTitle}",
            nameof(Core.Journal.JournalAction.ApplyFailed) => $"Could not apply — {TweakTitle}",
            nameof(Core.Journal.JournalAction.RevertFailed) => $"Could not undo — {TweakTitle}",
            nameof(Core.Journal.JournalAction.ApplyIntent) => $"Starting — {TweakTitle}",
            _ => $"{Line.Action} — {TweakTitle}",
        };

    /// <summary>Who or what did it, and what that means for undoing it.</summary>
    public string Explanation
    {
        get
        {
            var who = DescribeOrigin(Line.Origin);

            if (IsUnfinished)
            {
                return $"{who}, but it never finished — usually because the PC shut down partway "
                     + "through. Your old setting was saved first, so nothing was lost. Use Revert "
                     + "everything to be sure.";
            }

            return Line.Action switch
            {
                nameof(Core.Journal.JournalAction.ApplyCommitted) =>
                    $"{who}. Your previous setting was saved first, so this can be undone.",
                nameof(Core.Journal.JournalAction.RevertCommitted) =>
                    $"{who}. Your original setting was restored exactly as it was.",
                nameof(Core.Journal.JournalAction.ApplyFailed) =>
                    $"{who}. Nothing was changed, or what was changed was put back automatically.",
                nameof(Core.Journal.JournalAction.RevertFailed) =>
                    $"{who}. The setting is still changed. Try Revert everything.",
                _ => who + ".",
            };
        }
    }

    /// <summary>Emoji-free glyph, so it renders the same on every machine.</summary>
    public string Glyph => IsFailure ? "×" : IsUnfinished ? "!" : Line.Action switch
    {
        nameof(Core.Journal.JournalAction.RevertCommitted) => "↩",
        _ => "✓",
    };

    public string GlyphBrushKey => IsFailure ? "RiskHigh" : IsUnfinished ? "RiskModerate" : "RiskSafe";

    /// <summary>
    /// Turns an origin tag into who did it.
    ///
    /// These strings are written for somebody who does not know this program has a service.
    ///
    /// "watchdog" is a tag nothing writes any more -- the auto-revert timer that produced it
    /// has been removed. It stays here because the journal is append-only and a machine that
    /// ran an older build still has those lines in it, and a history entry that renders as
    /// "Source: watchdog" explains nothing to the person reading it.
    /// </summary>
    private static string DescribeOrigin(string origin)
    {
        if (origin.StartsWith("profile:", StringComparison.OrdinalIgnoreCase))
            return $"Part of the '{origin["profile:".Length..]}' preset";

        return origin.ToLowerInvariant() switch
        {
            "gui" => "You did this in this window",
            "cli" => "You did this from the command line",
            "manual" => "You asked for this",
            "watchdog" => "Undone automatically by the old safety timer, which no longer exists. "
                        + "Nothing undoes a change on its own any more",
            "reconcile" or "reconciler" => "Re-applied automatically, because Windows had changed "
                                         + "it back on its own",
            "service" => "Done by the background service",
            _ => $"Source: {origin}",
        };
    }

    /// <summary>
    /// Builds the display list, newest first, from raw journal lines.
    ///
    /// Two things happen here that make the list readable:
    ///
    /// <b>Intents are folded away.</b> Every change writes an "about to do this" row before it
    /// touches anything, which is what makes a crash mid-apply recoverable. It is bookkeeping,
    /// not an event -- so a matched pair collapses to the one row that says what happened. An
    /// intent with no outcome is the interesting case, and stays, saying so plainly.
    ///
    /// <b>Days get headers.</b> "21:50" means nothing without a date, and a full timestamp on
    /// every row is what made the old list read like a log file.
    /// </summary>
    public static IReadOnlyList<JournalEntryViewModel> Build(
        IEnumerable<JournalLine> lines,
        Func<string, string> titleFor)
    {
        var ordered = lines.OrderBy(l => l.TimestampUtc).ToList();
        var built = new List<JournalEntryViewModel>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var line = ordered[i];

            if (line.Action != nameof(Core.Journal.JournalAction.ApplyIntent))
            {
                built.Add(new JournalEntryViewModel(line, titleFor(line.TweakId)));
                continue;
            }

            // An intent is answered by the next entry for the same tweak. If that entry exists,
            // this row is noise; if it does not, the change was interrupted.
            var answered = ordered
                .Skip(i + 1)
                .Any(later => string.Equals(later.TweakId, line.TweakId, StringComparison.OrdinalIgnoreCase));

            if (!answered)
            {
                built.Add(new JournalEntryViewModel(line, titleFor(line.TweakId))
                {
                    IsUnfinished = true,
                });
            }
        }

        built.Reverse();

        // Headers are assigned after reversing so the first row of each day, reading downward,
        // is the one that carries the date.
        string? previousDay = null;
        foreach (var entry in built)
        {
            var day = DescribeDay(entry.Line.TimestampUtc.ToLocalTime());
            if (day != previousDay)
            {
                entry.DayHeader = day;
                previousDay = day;
            }
        }

        return built;
    }

    private static string DescribeDay(DateTimeOffset when)
    {
        var today = DateTimeOffset.Now.Date;
        var day = when.Date;

        if (day == today)
            return "Today";
        if (day == today.AddDays(-1))
            return "Yesterday";

        return day.Year == today.Year
            ? when.ToString("dddd d MMMM")
            : when.ToString("d MMMM yyyy");
    }
}
