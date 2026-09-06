using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;
using Nostos.Core.Localization;

namespace Nostos.App.Tests;

/// <summary>
/// What loading the window is allowed to cost.
///
/// Timings cannot be asserted -- a test machine under load would fail an honest build -- so what
/// is held here is the shape that made the window slow rather than the milliseconds it took:
/// how many reads a refresh performs, and whether a read that found nothing new still churns
/// every row on screen. Both were measured on a real machine before they were fixed, and both
/// are invisible to every other test in the suite.
/// </summary>
public sealed class StartupCostTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static TweakChoice Level(string defaultOption = "balanced") => new()
    {
        Id = "level",
        Title = "Level",
        Description = "How hard to push it.",
        DefaultOption = defaultOption,
        Options =
        [
            new TweakChoiceOption { Id = "balanced", Title = "Balanced", Description = "The safe one." },
            new TweakChoiceOption { Id = "max", Title = "Maximum", Description = "The loud one." },
        ],
    };

    private static async Task<(MainWindowViewModel Window, FakeBackend Backend)> LoadedAsync(int withChoices)
    {
        var backend = new FakeBackend();

        for (var i = 0; i < withChoices; i++)
            backend.Statuses.Add(FakeBackend.Tweak($"choice.tweak{i}", choices: [Level()]));

        backend.Statuses.Add(FakeBackend.Tweak("plain.tweak"));

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        return (window, backend);
    }

    [Fact]
    public async Task Loading_reads_the_catalog_once_and_asks_nothing_twice()
    {
        // The bulk read resolves every choice to its default, so a row still on its defaults has
        // already been answered. Re-reading them anyway cost fifty extra calls per refresh on
        // this machine -- fifty round trips over the pipe on an installed copy -- and the
        // refresh runs every five seconds for as long as the window is open.
        var (_, backend) = await LoadedAsync(withChoices: 12);

        Assert.Empty(backend.StatusReads);
    }

    [Fact]
    public async Task A_setting_the_user_changed_is_re_read_and_the_rest_are_not()
    {
        // The saving must not cost the correctness it was paying for: a row whose selection is
        // not the default has genuinely not been read under that selection.
        var (window, backend) = await LoadedAsync(withChoices: 12);

        var row = window.Tweaks.First(t => t.HasChoices);
        row.Choices[0].Options.Single(o => o.Id == "max").IsChecked = true;

        backend.StatusReads.Clear();
        await window.RefreshCommand.ExecuteAsync(null);

        var read = Assert.Single(backend.StatusReads);
        Assert.Equal(row.Id, read.TweakId);
        Assert.Equal("max", read.Options?["level"]);
    }

    [Fact]
    public void A_row_told_the_same_thing_twice_does_not_announce_a_change()
    {
        // Every assignment used to invalidate every property of every row. The catalog is
        // re-read every five seconds, so that was a full re-layout of the list four times a
        // minute to redraw what was already there.
        var first = FakeBackend.Tweak("a.tweak");
        var second = FakeBackend.Tweak("a.tweak");

        Assert.True(TweakItemViewModel.SameState(first, second));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void A_row_whose_answer_moved_does_announce_it(bool applied, bool managed, bool applicable)
    {
        var before = FakeBackend.Tweak("a.tweak");
        var after = FakeBackend.Tweak(
            "a.tweak", applied: applied, managed: managed, applicable: !applicable);

        Assert.False(TweakItemViewModel.SameState(before, after));
    }

    [Fact]
    public void A_changed_state_description_counts_as_a_change()
    {
        // The raw read is on the row -- "HwSchMode = 2" -- and it can move while every flag
        // stays put. Comparing only the booleans would leave the number on screen stale.
        var before = FakeBackend.Tweak("a.tweak");
        var after = before with { StateDescription = "HwSchMode = 2" };

        Assert.False(TweakItemViewModel.SameState(before, after));
    }

    [Fact]
    public async Task The_startup_read_is_quiet_unless_it_is_slow()
    {
        // The indicator shows after a delay and then holds for its minimum, which is right for
        // work somebody asked for. Nobody asks for the load that fills the window, and at the
        // usual threshold it announced itself and then outlasted itself: the window went on
        // saying "reading the catalog" for half a second after it had finished.
        var tracker = new ActivityTracker { ShowAfter = TimeSpan.FromMilliseconds(180) };

        await using (tracker.Begin("reading", showAfter: TimeSpan.FromMilliseconds(700)))
            await Task.Delay(TimeSpan.FromMilliseconds(60));

        Assert.False(tracker.IsVisible);
    }
}
