using Nostos.Core.Localization;
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
    /// <summary>
    /// True for a startup entry being switched rather than a tweak being applied.
    ///
    /// These lines are a different kind of event and read badly in the tweak vocabulary:
    /// "Applied -- Steam" is not what happened, and "Your previous setting was saved first, so
    /// this can be undone" is not true of something that was never captured.
    /// </summary>
    public bool IsStartup => Core.Journal.StartupJournal.Owns(Line.TweakId);

    private bool StartupEnabled => Core.Journal.StartupJournal.WasEnabled(Line.Detail);

    private string StartupName => Core.Journal.StartupJournal.NameOf(Line.TweakId, Line.Detail);

    public string Headline => IsStartup
        ? Strings.Format(
            StartupEnabled ? "journal.headline.startupon" : "journal.headline.startupoff",
            StartupName)
        : IsUnfinished
        ? Strings.Format("journal.headline.interrupted", TweakTitle)
        : Line.Action switch
        {
            nameof(Core.Journal.JournalAction.ApplyCommitted) =>
                Strings.Format("journal.headline.applied", TweakTitle),
            nameof(Core.Journal.JournalAction.RevertCommitted) =>
                Strings.Format("journal.headline.undone", TweakTitle),
            nameof(Core.Journal.JournalAction.ApplyFailed) =>
                Strings.Format("journal.headline.applyfailed", TweakTitle),
            nameof(Core.Journal.JournalAction.RevertFailed) =>
                Strings.Format("journal.headline.revertfailed", TweakTitle),
            nameof(Core.Journal.JournalAction.ApplyIntent) =>
                Strings.Format("journal.headline.starting", TweakTitle),
            _ => Strings.Format("journal.headline.other", Line.Action, TweakTitle),
        };

    /// <summary>Who or what did it, and what that means for undoing it.</summary>
    public string Explanation
    {
        get
        {
            var who = DescribeOrigin(Line.Origin);

            if (IsStartup)
            {
                return Strings.Get(StartupEnabled
                    ? "journal.explain.startupon"
                    : "journal.explain.startupoff");
            }

            if (IsUnfinished)
                return Strings.Format("journal.explain.interrupted", who);

            return Line.Action switch
            {
                nameof(Core.Journal.JournalAction.ApplyCommitted) =>
                    Strings.Format("journal.explain.applied", who),
                nameof(Core.Journal.JournalAction.RevertCommitted) =>
                    Strings.Format("journal.explain.undone", who),
                nameof(Core.Journal.JournalAction.ApplyFailed) =>
                    Strings.Format("journal.explain.applyfailed", who),
                nameof(Core.Journal.JournalAction.RevertFailed) =>
                    Strings.Format("journal.explain.revertfailed", who),
                _ => Strings.Format("journal.explain.other", who),
            };
        }
    }

    /// <summary>Emoji-free glyph, so it renders the same on every machine.</summary>
    public string Glyph => IsStartup ? (StartupEnabled ? "▲" : "▼")
        : IsFailure ? "×" : IsUnfinished ? "!" : Line.Action switch
    {
        nameof(Core.Journal.JournalAction.RevertCommitted) => "↩",
        _ => "✓",
    };

    public string GlyphBrushKey => IsStartup ? (StartupEnabled ? "RiskSafe" : "Muted")
        : IsFailure ? "RiskHigh" : IsUnfinished ? "RiskModerate" : "RiskSafe";

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
            return Strings.Format("journal.origin.profile", origin["profile:".Length..]);

        return origin.ToLowerInvariant() switch
        {
            "gui" => Strings.Get("journal.origin.gui"),
            "cli" => Strings.Get("journal.origin.cli"),
            "manual" => Strings.Get("journal.origin.manual"),
            "watchdog" => Strings.Get("journal.origin.watchdog"),
            "reconcile" or "reconciler" => Strings.Get("journal.origin.reconcile"),
            "service" => Strings.Get("journal.origin.service"),
            _ => Strings.Format("journal.origin.unknown", origin),
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
            return Strings.Get("journal.day.today");
        if (day == today.AddDays(-1))
            return Strings.Get("journal.day.yesterday");

        // The month's name is text like any other, so it follows the language the user chose
        // rather than the one Windows is installed in. A German window that says "25 August"
        // in one row and "Gestern" in the next is worse than either on its own.
        return Strings.DateText(when, withYear: day.Year != today.Year);
    }
}
