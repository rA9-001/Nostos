using Nostos.Core.Engine;
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
            $"Applied — {title}",
            result.RequiresReboot
                ? "Restart your PC for this to take effect. Until then nothing has changed for you."
                : "Your previous setting was saved, so this can be undone at any time.",
            IsProblem: false),

        Outcome.AlreadyApplied => new(
            $"Already set — {title}",
            "This was already how you wanted it, so nothing was changed.",
            IsProblem: false),

        Outcome.Reverted => new(
            $"Undone — {title}",
            "Your original setting is back, exactly as it was before.",
            IsProblem: false),

        Outcome.NothingToRevert => new(
            $"Nothing to undo — {title}",
            "This program never changed this setting, so there is nothing to put back.",
            IsProblem: false),

        // Skipped is the one where the reason is the whole message: not applicable here, not
        // elevated, conflicts with something else. Passing it through is right.
        Outcome.Skipped => new(
            $"Skipped — {title}",
            Sentence(result.Message),
            IsProblem: false),

        Outcome.RolledBack => new(
            $"Did not work — {title}",
            "Something went wrong partway through, so your original setting was put back "
            + "automatically. Your PC is as it was.",
            IsProblem: true),

        Outcome.Unverified => new(
            $"Applied, but could not confirm — {title}",
            "The change was made and can be undone, but reading it back did not confirm it. "
            + "Something else on this PC may be overriding it.",
            IsProblem: true),

        _ => new(
            $"Could not apply — {title}",
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
            return new($"Nothing to do — {what}", "No settings needed changing.", IsProblem: false);

        var changed = results.Count(r => r.Outcome is Outcome.Applied or Outcome.Reverted);
        var already = results.Count(r => r.Outcome is Outcome.AlreadyApplied or Outcome.NothingToRevert);
        var skipped = results.Count(r => r.Outcome is Outcome.Skipped);
        var problems = results.Where(r => r.Outcome
            is Outcome.Failed or Outcome.RolledBack or Outcome.Unverified).ToList();

        var headline = changed == 0
            ? $"No changes needed — {what}"
            : $"{Count(changed, "change")} made — {what}";

        var notes = new List<string>();
        if (already > 0)
            notes.Add($"{already} already set the way you wanted");
        if (skipped > 0)
            notes.Add($"{skipped} skipped (not applicable to this PC, or needs administrator)");
        if (problems.Count > 0)
            notes.Add($"{Count(problems.Count, "problem")}: {problems[0].Message}");

        if (results.Any(r => r.RequiresReboot))
            notes.Add("restart your PC for everything to take effect");

        return new(
            headline,
            notes.Count == 0
                ? "Your previous settings were saved, so all of this can be undone."
                : Sentence(string.Join("; ", notes)),
            IsProblem: problems.Count > 0);
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

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
