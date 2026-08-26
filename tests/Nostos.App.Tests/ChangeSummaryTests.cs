using Nostos.App.ViewModels;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// What the activity panel says after a change.
///
/// The engine's own summary is precise and unreadable — a dotted id, an enum name and a registry
/// assignment. These pin down the translation, and in particular that every outcome ends up
/// saying what state the machine is now in, because that is the only question being asked.
/// </summary>
public sealed class ChangeSummaryTests
{
    private const string Title = "Turn off mouse acceleration";

    private static ChangeResult Result(
        Outcome outcome, string message = "ok", bool reboot = false, string id = "a.one")
        => new(id, outcome, message, reboot);

    private static ChangeSummary One(Outcome outcome, string message = "ok", bool reboot = false)
        => ChangeSummary.ForOne(Result(outcome, message, reboot), Title);

    [Fact]
    public void An_applied_change_says_it_can_be_undone()
    {
        var summary = One(Outcome.Applied);

        Assert.Equal($"Applied — {Title}", summary.Headline);
        Assert.Contains("can be undone", summary.Detail);
        Assert.False(summary.IsProblem);
    }

    [Fact]
    public void The_verb_goes_in_front_of_the_title_not_around_it()
    {
        // Titles are already imperative, so "Turned on Turn off mouse acceleration" is what the
        // obvious phrasing produces. This is the assertion that stops that coming back.
        var summary = One(Outcome.Applied);

        Assert.DoesNotContain("Turned on Turn off", summary.Headline);
        Assert.StartsWith("Applied", summary.Headline);
        Assert.EndsWith(Title, summary.Headline);
    }

    [Fact]
    public void A_change_that_needs_a_restart_says_so_instead_of_promising_an_effect()
    {
        // Otherwise the user goes looking for a difference that cannot be there yet, decides the
        // tool did nothing, and applies five more things to compensate.
        var summary = One(Outcome.Applied, reboot: true);

        Assert.Contains("Restart", summary.Detail);
        Assert.Contains("nothing has changed for you", summary.Detail);
    }

    [Fact]
    public void An_undo_says_the_original_is_back()
    {
        var summary = One(Outcome.Reverted);

        Assert.Equal($"Undone — {Title}", summary.Headline);
        Assert.Contains("exactly as it was", summary.Detail);
    }

    [Fact]
    public void Nothing_having_happened_is_stated_plainly()
    {
        Assert.Contains("Already set", One(Outcome.AlreadyApplied).Headline);
        Assert.Contains("nothing was changed", One(Outcome.AlreadyApplied).Detail);

        Assert.Contains("Nothing to undo", One(Outcome.NothingToRevert).Headline);
    }

    [Fact]
    public void A_rollback_reassures_that_the_machine_is_unchanged()
    {
        // The worst moment to be terse. Something failed; the one thing worth saying is that the
        // PC is as it was.
        var summary = One(Outcome.RolledBack);

        Assert.Contains("Did not work", summary.Headline);
        Assert.Contains("your original setting was put back", summary.Detail);
        Assert.Contains("as it was", summary.Detail);
        Assert.True(summary.IsProblem);
    }

    [Fact]
    public void An_unverified_apply_admits_what_it_does_not_know()
    {
        var summary = One(Outcome.Unverified);

        Assert.Contains("could not confirm", summary.Headline);
        Assert.Contains("overriding it", summary.Detail);
        Assert.True(summary.IsProblem);
    }

    [Fact]
    public void A_skip_passes_the_reason_through_because_the_reason_is_the_message()
    {
        var summary = One(Outcome.Skipped, "needs an elevated launch");

        Assert.Contains("Skipped", summary.Headline);
        Assert.Equal("Needs an elevated launch.", summary.Detail);
        Assert.False(summary.IsProblem);
    }

    [Fact]
    public void Engine_messages_are_punctuated_so_they_read_as_prose()
    {
        // They arrive lowercase and unterminated, which looks like a leaked log line sitting
        // next to sentences we wrote.
        var summary = One(Outcome.Failed, "access denied");

        Assert.Equal("Access denied.", summary.Detail);
    }

    [Fact]
    public void A_batch_is_counted_not_listed()
    {
        var summary = ChangeSummary.ForMany(
            [Result(Outcome.Applied), Result(Outcome.Applied), Result(Outcome.AlreadyApplied)],
            "the 'competitive' preset");

        Assert.Equal("2 changes made — the 'competitive' preset", summary.Headline);
        Assert.Contains("1 already set", summary.Detail);
        Assert.False(summary.IsProblem);
    }

    [Fact]
    public void One_change_is_singular()
    {
        var summary = ChangeSummary.ForMany([Result(Outcome.Applied)], "the 'streaming' preset");

        Assert.StartsWith("1 change made", summary.Headline);
        Assert.DoesNotContain("1 changes", summary.Headline);
    }

    [Fact]
    public void A_batch_that_changed_nothing_says_so_rather_than_reporting_zero()
    {
        var summary = ChangeSummary.ForMany(
            [Result(Outcome.AlreadyApplied), Result(Outcome.AlreadyApplied)], "the 'conservative' preset");

        Assert.StartsWith("No changes needed", summary.Headline);
    }

    [Fact]
    public void Problems_in_a_batch_are_surfaced_not_averaged_away()
    {
        // "8 changed, 2 failed" hides which two and why. The first reason is worth more than the
        // count on its own.
        var summary = ChangeSummary.ForMany(
            [Result(Outcome.Applied), Result(Outcome.Failed, "access denied")],
            "the 'competitive' preset");

        Assert.True(summary.IsProblem);
        Assert.Contains("1 problem", summary.Detail);
        Assert.Contains("access denied", summary.Detail);
    }

    [Fact]
    public void A_batch_needing_a_restart_says_so_once()
    {
        var summary = ChangeSummary.ForMany(
            [Result(Outcome.Applied), Result(Outcome.Applied, reboot: true)], "the 'competitive' preset");

        Assert.Contains("restart your PC", summary.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_batch_is_not_an_error()
    {
        var summary = ChangeSummary.ForMany([], "the 'streaming' preset");

        Assert.StartsWith("Nothing to do", summary.Headline);
        Assert.False(summary.IsProblem);
    }

    [Fact]
    public void No_summary_leaks_a_tweak_id_or_a_run_together_enum_name()
    {
        // The two things that made the old line unreadable. "Applied" and "Skipped" are
        // ordinary words and are allowed; "AlreadyApplied" and "NothingToRevert" are not, so
        // the check is for the run-together multi-word names rather than every enum member.
        foreach (var outcome in Enum.GetValues<Outcome>())
        {
            var summary = One(outcome);
            var name = outcome.ToString();

            Assert.DoesNotContain("a.one", summary.Headline);

            if (name.Skip(1).Any(char.IsUpper))
                Assert.DoesNotContain(name, summary.Headline);

            Assert.False(string.IsNullOrWhiteSpace(summary.Detail), name);
        }
    }
}
