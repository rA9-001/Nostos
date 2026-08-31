using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Localization;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// Watching a profile being applied.
///
/// Forty-two tweaks is fifteen seconds of nothing moving, and "is it stuck, or is it working?"
/// is the only question a reader has during it. What these tests hold is that the answer on
/// screen is the truth: the reports come from inside the loop that does the work, so the card
/// stops on the tweak that stops rather than animating cheerfully past it.
///
/// The thing that would be easiest to build and worst to ship is an animation timed to look
/// plausible. There is no test that could tell you it was lying, which is exactly why the
/// progress is plumbed through the backend instead.
/// </summary>
public sealed class ProfileProgressTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static ProfileViewModel Card(params string[] ids)
        => new(
            new ProfileSummary("basic", "A profile.", ids.Length, ids),
            id => new TweakItemViewModel(FakeBackend.Tweak(id, title: id)));

    private static async Task<MainWindowViewModel> WindowAsync(FakeBackend backend)
    {
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        return window;
    }

    private static FakeBackend BackendWith(string name, params (string Id, Outcome Outcome)[] rows)
    {
        var backend = new FakeBackend();
        backend.Statuses.AddRange(rows.Select(r => FakeBackend.Tweak(r.Id, title: r.Id)));
        backend.ProfileList.Add(new ProfileSummary(name, "A profile.", rows.Length, [.. rows.Select(r => r.Id)]));
        backend.ProfileResults[name] = [.. rows.Select(r => new ChangeResult(r.Id, r.Outcome, "done", false))];
        return backend;
    }

    [Fact]
    public void A_card_that_is_not_being_applied_shows_no_progress_at_all()
    {
        var card = Card("a.one", "a.two");

        Assert.False(card.IsApplying);
        Assert.False(card.HasCountedProgress);
        Assert.Equal("", card.ProgressText);
        Assert.All(card.Tweaks, t => Assert.Equal(RowRunState.Idle, t.RunState));
    }

    [Fact]
    public void Starting_a_run_opens_the_card()
    {
        // Otherwise there is a bar and nothing else. Being able to see which tweak the program
        // is on is the entire point, and a collapsed card shows none of them.
        var card = Card("a.one", "a.two");

        card.BeginRun();

        Assert.True(card.IsExpanded);
        Assert.True(card.IsApplying);
    }

    [Fact]
    public void One_row_at_a_time_is_marked_as_running()
    {
        var card = Card("a.one", "a.two", "a.three");
        card.BeginRun();

        card.Report(new BatchProgress(1, 3, "a.one"));
        Assert.Equal([RowRunState.Running, RowRunState.Idle, RowRunState.Idle],
            card.Tweaks.Select(t => t.RunState));

        card.Report(new BatchProgress(1, 3, "a.one", Outcome.Applied));
        card.Report(new BatchProgress(2, 3, "a.two"));
        Assert.Equal([RowRunState.Done, RowRunState.Running, RowRunState.Idle],
            card.Tweaks.Select(t => t.RunState));
    }

    [Fact]
    public void A_running_row_shows_a_spinner_instead_of_a_stale_glyph()
    {
        // While a row is being applied its state is not knowable. A tick still sitting beside a
        // spinner invites the reader to believe it, which is the same mistake the tweak rows
        // were fixed for.
        var card = Card("a.one");
        card.BeginRun();
        card.Report(new BatchProgress(1, 1, "a.one"));

        var row = card.Tweaks.Single();

        Assert.True(row.IsRunning);
        Assert.False(row.HasGlyph);
    }

    [Theory]
    [InlineData(Outcome.Applied, RowRunState.Done)]
    [InlineData(Outcome.AlreadyApplied, RowRunState.Done)]
    [InlineData(Outcome.Unverified, RowRunState.Done)]
    [InlineData(Outcome.Skipped, RowRunState.Skipped)]
    [InlineData(Outcome.Failed, RowRunState.Failed)]
    [InlineData(Outcome.RolledBack, RowRunState.Failed)]
    public void Each_outcome_leaves_the_row_saying_what_happened(Outcome outcome, RowRunState expected)
    {
        // A skip is not a failure -- it is a deliberate, explained decision, usually "not
        // applicable on this PC" -- and painting it red would teach people to distrust the red.
        var card = Card("a.one");
        card.BeginRun();
        card.Report(new BatchProgress(1, 1, "a.one", outcome));

        Assert.Equal(expected, card.Tweaks.Single().RunState);
    }

    [Fact]
    public void A_failed_row_is_told_apart_from_a_skipped_one_by_more_than_its_colour()
    {
        var card = Card("a.one", "a.two");
        card.BeginRun();
        card.Report(new BatchProgress(1, 2, "a.one", Outcome.Skipped));
        card.Report(new BatchProgress(2, 2, "a.two", Outcome.Failed));

        Assert.NotEqual(card.Tweaks[0].StateGlyph, card.Tweaks[1].StateGlyph);
        Assert.Equal("DangerText", card.Tweaks[1].StateBrushKey);
    }

    [Fact]
    public void The_count_only_moves_when_a_tweak_has_actually_finished()
    {
        // Counting the "starting" report would put the bar one tweak ahead of the machine for
        // the whole run, and would show 42 of 42 while the last one was still being written.
        var card = Card("a.one", "a.two");
        card.BeginRun();

        card.Report(new BatchProgress(1, 2, "a.one"));
        Assert.Equal("0 of 2", card.ProgressText);

        card.Report(new BatchProgress(1, 2, "a.one", Outcome.Applied));
        Assert.Equal("1 of 2", card.ProgressText);
        Assert.Equal(0.5, card.ProgressFraction);
    }

    [Fact]
    public void The_bar_stays_indeterminate_until_a_real_report_arrives()
    {
        // And stays indeterminate for good on a backend that cannot report one. A determinate
        // bar that is never fed reads "0 of 42" for the whole run and then jumps, which is a
        // worse lie than admitting to not knowing where it is.
        var card = Card("a.one", "a.two");
        card.BeginRun();

        Assert.False(card.HasCountedProgress);

        card.Report(new BatchProgress(1, 2, "a.one"));
        Assert.True(card.HasCountedProgress);
    }

    [Fact]
    public void A_report_for_a_tweak_this_card_does_not_have_is_ignored_rather_than_thrown()
    {
        // Defensive: a profile on disk and a catalog in memory can disagree, and a batch that
        // resolved differently must not take the window down mid-apply.
        var card = Card("a.one");
        card.BeginRun();

        card.Report(new BatchProgress(1, 1, "something.else", Outcome.Applied));

        Assert.Equal(RowRunState.Idle, card.Tweaks.Single().RunState);
        Assert.Equal("1 of 1", card.ProgressText);
    }

    [Fact]
    public void The_outcomes_stay_on_screen_when_the_run_ends()
    {
        // The reader has just watched forty rows go past; the state they end in is the answer
        // to "did that work". They are cleared by the refresh that follows, which rebuilds them
        // from what the machine now reports -- the more trustworthy of the two answers.
        var card = Card("a.one");
        card.BeginRun();
        card.Report(new BatchProgress(1, 1, "a.one", Outcome.Applied));
        card.EndRun();

        Assert.False(card.IsApplying);
        Assert.Equal(RowRunState.Done, card.Tweaks.Single().RunState);
    }

    [Fact]
    public async Task Applying_a_profile_from_the_window_walks_the_card()
    {
        // End to end through the view model, because the wiring is where this would break: a
        // card that is applied but never told about it shows nothing at all, and every unit
        // test above would still pass.
        var backend = BackendWith("basic",
            ("a.one", Outcome.Applied),
            ("a.two", Outcome.Skipped));

        var window = await WindowAsync(backend);
        var card = window.Profiles.Single(p => p.Name == "basic");
        var seen = new List<(string Id, RowRunState State, string Text)>();

        backend.WhileRunning = progress =>
        {
            var row = card.Tweaks.Single(t => t.Id == progress.TweakId);
            seen.Add((row.Id, row.RunState, card.ProgressText));
            return Task.CompletedTask;
        };

        await window.ApplyProfileCommand.ExecuteAsync(card);

        Assert.Equal(
            [("a.one", RowRunState.Running, "0 of 2"), ("a.two", RowRunState.Running, "1 of 2")],
            seen);

        Assert.Equal(["basic"], backend.ProfilesApplied);
        Assert.False(card.IsApplying);
    }

    [Fact]
    public async Task A_refresh_during_a_run_does_not_replace_the_card_being_applied()
    {
        // The live loop reloads the profiles every few seconds. It used to clear the list and
        // rebuild it, which would drop the object the progress reports are being delivered to
        // and leave the run invisible from that second onwards.
        var backend = BackendWith("basic", ("a.one", Outcome.Applied));
        var window = await WindowAsync(backend);

        var before = window.Profiles.Single(p => p.Name == "basic");
        await window.RefreshCommand.ExecuteAsync(null);

        Assert.Same(before, window.Profiles.Single(p => p.Name == "basic"));
    }

    [Fact]
    public void The_progress_line_is_translated()
    {
        var card = Card("a.one", "a.two");
        card.BeginRun();
        card.Report(new BatchProgress(1, 2, "a.one", Outcome.Applied));

        Strings.Language = Language.German;
        card.RefreshText();

        Assert.Equal("1 von 2", card.ProgressText);
    }
}
