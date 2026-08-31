using Nostos.Core.Engine;
using Nostos.Core.Localization;
using Nostos.Ipc;

namespace Nostos.App.ViewModels;

/// <summary>
/// What just happened, in two lines a person can act on.
///
/// The engine's own answer is precise and unreadable:
/// <c>mmcss.system-responsiveness: applied — SystemResponsiveness = 10 [Background CPU
/// reservation: Balanced - 10%]</c>. That is a dotted identifier, an enum name and a registry
/// assignment, which is the right record to keep and the wrong thing to put in front of somebody
/// who has just clicked a button.
///
/// So it splits: a headline saying what happened, and a second line saying what it means or what
/// to do next. The exact values are still there, in the History tab, where the point is proof
/// rather than reassurance.
/// </summary>
/// <param name="Headline">One clause. What happened.</param>
/// <param name="Detail">What it means, what to do next, or null to leave the standing line.</param>
/// <param name="IsProblem">True when the user needs to notice.</param>
public readonly record struct ChangeSummary(string Headline, string? Detail, bool IsProblem)
{
    /// <summary>
    /// Summarises one tweak's result.
    ///
    /// The tweak's title is used as-is rather than bent into a sentence. Titles here are already
    /// imperative — "Turn off mouse acceleration", "Stop Store apps running in the background" —
    /// so wrapping them in a verb produces "Turned on Turn off mouse acceleration". The verb
    /// belongs on its own, in front.
    /// </summary>
    public static ChangeSummary ForOne(ChangeResult result, string title) => result.Outcome switch
    {
        Outcome.Applied => new(
            Strings.Format("summary.applied", title),
            Strings.Get(result.RequiresReboot
                ? "summary.applied.reboot"
                : "summary.applied.undoable"),
            IsProblem: false),

        Outcome.AlreadyApplied => new(
            Strings.Format("summary.alreadyset", title),
            Strings.Get("summary.alreadyset.detail"),
            IsProblem: false),

        Outcome.Reverted => new(
            Strings.Format("summary.undone", title),
            Strings.Get("summary.undone.detail"),
            IsProblem: false),

        Outcome.NothingToRevert => new(
            Strings.Format("summary.nothingtoundo", title),
            Strings.Get("summary.nothingtoundo.detail"),
            IsProblem: false),

        // Skipped is the one where the reason is the whole message: not applicable here, not
        // elevated, conflicts with something else. Passing it through is right.
        Outcome.Skipped => new(
            Strings.Format("summary.skipped", title),
            Sentence(result.Message),
            IsProblem: false),

        Outcome.RolledBack => new(
            Strings.Format("summary.rolledback", title),
            Strings.Get("summary.rolledback.detail"),
            IsProblem: true),

        Outcome.Unverified => new(
            Strings.Format("summary.unverified", title),
            Strings.Get("summary.unverified.detail"),
            IsProblem: true),

        _ => new(
            Strings.Format("summary.failed", title),
            Sentence(result.Message),
            IsProblem: true),
    };

    /// <summary>
    /// Summarises a batch: a preset, or Revert everything.
    ///
    /// Counts rather than a list, because a preset can touch ten things and ten lines is not a
    /// summary. Anything that went wrong is named in the second line, since that is the part
    /// worth reading.
    /// </summary>
    public static ChangeSummary ForMany(IReadOnlyList<ChangeResult> results, string what)
    {
        if (results.Count == 0)
        {
            return new(
                Strings.Format("summary.nothingtodo", what),
                Strings.Get("summary.nothingtodo.detail"),
                IsProblem: false);
        }

        var changed = results.Count(r => r.Outcome is Outcome.Applied or Outcome.Reverted);
        var already = results.Count(r => r.Outcome is Outcome.AlreadyApplied or Outcome.NothingToRevert);
        var skipped = results.Count(r => r.Outcome is Outcome.Skipped);
        var problems = results.Where(r => r.Outcome
            is Outcome.Failed or Outcome.RolledBack or Outcome.Unverified).ToList();

        var headline = changed == 0
            ? Strings.Format("summary.nochanges", what)
            : Strings.Format(
                "summary.changesmade", Strings.Plural("summary.count.change", changed), what);

        var notes = new List<string>();
        if (already > 0)
            notes.Add(Strings.Format("summary.note.already", already));
        if (skipped > 0)
            notes.Add(Strings.Format("summary.note.skipped", skipped));
        if (problems.Count > 0)
        {
            notes.Add(Strings.Format(
                "summary.note.problems",
                Strings.Plural("summary.count.problem", problems.Count),
                problems[0].Message));
        }

        if (results.Any(r => r.RequiresReboot))
            notes.Add(Strings.Get("summary.note.reboot"));

        return new(
            headline,
            notes.Count == 0
                ? Strings.Get("summary.alldone")
                : Sentence(string.Join("; ", notes)),
            IsProblem: problems.Count > 0);
    }

    /// <summary>Capitalises and full-stops an engine message so it reads as prose beside ours.</summary>
    private static string? Sentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var capitalised = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];

        return capitalised.EndsWith('.') ? capitalised : capitalised + ".";
    }
}
