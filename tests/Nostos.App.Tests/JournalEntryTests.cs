using Nostos.App.ViewModels;
using Nostos.Core.Journal;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// The change log, as a person reads it.
///
/// The journal on disk is a machine record and has to stay one. What these pin down is the
/// translation: an "ApplyCommitted / mmcss.system-responsiveness / gui" row has to come out as
/// a sentence somebody who has never opened a registry editor can act on.
/// </summary>
public sealed class JournalEntryTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static JournalLine Line(
        string action,
        string tweakId = "a.one",
        string origin = "gui",
        string? detail = null,
        string? error = null,
        int minutesAgo = 0)
        => new(Noon.AddMinutes(-minutesAgo), tweakId, action, origin, detail, error);

    private static IReadOnlyList<JournalEntryViewModel> Build(params JournalLine[] lines)
        => JournalEntryViewModel.Build(lines, _ => "Turn off mouse acceleration");

    [Fact]
    public void An_applied_change_reads_as_a_sentence()
    {
        var entry = Build(Line(nameof(JournalAction.ApplyCommitted))).Single();

        Assert.Equal("Applied — Turn off mouse acceleration", entry.Headline);
        Assert.Contains("You did this in this window", entry.Explanation);
        Assert.Contains("can be undone", entry.Explanation);
    }

    [Fact]
    public void A_revert_says_the_original_was_restored()
    {
        var entry = Build(Line(nameof(JournalAction.RevertCommitted))).Single();

        Assert.Equal("Undone — Turn off mouse acceleration", entry.Headline);
        Assert.Contains("restored exactly as it was", entry.Explanation);
    }

    [Fact]
    public void The_tweak_is_named_not_identified()
    {
        // The id is what the journal stores. Nobody reading their own change history should
        // have to know that "mmcss.system-responsiveness" is the CPU reservation setting.
        var entry = Build(Line(nameof(JournalAction.ApplyCommitted), "mmcss.system-responsiveness")).Single();

        Assert.DoesNotContain("mmcss", entry.Headline);
        Assert.Contains("Turn off mouse acceleration", entry.Headline);
    }

    [Fact]
    public void An_unknown_tweak_falls_back_to_its_id_rather_than_going_blank()
    {
        // Entries outlive the tweaks that made them. A record of a change you can no longer
        // name is worse than an ugly name.
        var built = JournalEntryViewModel.Build(
            [Line(nameof(JournalAction.ApplyCommitted), "removed.tweak")], id => id);

        Assert.Contains("removed.tweak", built.Single().Headline);
    }

    [Fact]
    public void Bookkeeping_rows_are_folded_away()
    {
        // Every change writes an intent before touching anything, which is what makes a crash
        // recoverable. It is not an event a person needs to read.
        var built = Build(
            Line(nameof(JournalAction.ApplyIntent), minutesAgo: 2),
            Line(nameof(JournalAction.ApplyCommitted), minutesAgo: 1));

        var entry = Assert.Single(built);
        Assert.Equal("Applied — Turn off mouse acceleration", entry.Headline);
    }

    [Fact]
    public void An_intent_with_no_outcome_is_the_one_that_survives()
    {
        // A change that was started and never finished is the single journal state that asks
        // the reader to do something, so it must not be folded away with the rest.
        var entry = Assert.Single(Build(Line(nameof(JournalAction.ApplyIntent))));

        Assert.True(entry.IsUnfinished);
        Assert.Contains("Interrupted", entry.Headline);
        Assert.Contains("never finished", entry.Explanation);
        Assert.Contains("Revert everything", entry.Explanation);
    }

    [Fact]
    public void A_failure_says_what_state_the_machine_is_in()
    {
        var applyFailed = Build(Line(nameof(JournalAction.ApplyFailed))).Single();
        Assert.True(applyFailed.IsFailure);
        Assert.Contains("Nothing was changed", applyFailed.Explanation);

        var revertFailed = Build(Line(nameof(JournalAction.RevertFailed))).Single();
        Assert.True(revertFailed.IsFailure);
        Assert.Contains("still changed", revertFailed.Explanation);
    }

    [Theory]
    [InlineData("gui", "You did this in this window")]
    [InlineData("cli", "command line")]
    [InlineData("profile:competitive", "'competitive' preset")]
    [InlineData("watchdog", "safety timer")]
    [InlineData("reconcile", "Windows had changed it back")]
    public void The_origin_is_explained_rather_than_printed(string origin, string expected)
    {
        // "watchdog" is a tag nothing writes any more, but the journal is append-only, so an
        // older machine's history still contains it and still has to render as English.
        var entry = Build(Line(nameof(JournalAction.ApplyCommitted), origin: origin)).Single();

        Assert.Contains(expected, entry.Explanation);
    }

    [Fact]
    public void Entries_are_newest_first()
    {
        var built = Build(
            Line(nameof(JournalAction.ApplyCommitted), "a.old", minutesAgo: 90),
            Line(nameof(JournalAction.ApplyCommitted), "a.new", minutesAgo: 1));

        Assert.Equal("a.new", built[0].Line.TweakId);
        Assert.Equal("a.old", built[1].Line.TweakId);
    }

    [Fact]
    public void Only_the_first_entry_of_a_day_carries_the_date()
    {
        var built = Build(
            Line(nameof(JournalAction.ApplyCommitted), "a.1", minutesAgo: 1),
            Line(nameof(JournalAction.ApplyCommitted), "a.2", minutesAgo: 2),
            Line(nameof(JournalAction.ApplyCommitted), "a.3", minutesAgo: 60 * 30));

        Assert.True(built[0].HasDayHeader);
        Assert.False(built[1].HasDayHeader);
        Assert.True(built[2].HasDayHeader);
        Assert.NotEqual(built[0].DayHeader, built[2].DayHeader);
    }

    [Fact]
    public void The_exact_values_are_kept_underneath()
    {
        // Demoted, not dropped. A tool whose whole claim is that it can prove what it changed
        // does not get to hide the proof.
        var entry = Build(Line(
            nameof(JournalAction.ApplyCommitted),
            detail: "MouseSpeed = 0; MouseThreshold1 = 0")).Single();

        Assert.Equal("MouseSpeed = 0; MouseThreshold1 = 0", entry.Detail);
    }
}
