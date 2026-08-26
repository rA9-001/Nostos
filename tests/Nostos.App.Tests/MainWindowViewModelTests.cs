using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;

namespace Nostos.App.Tests;

/// <summary>
/// Catalog filtering, and the selection bookkeeping around it.
///
/// Both of the bugs found while first running the window lived here: rebuilding the bound
/// category collection reset the selection to null, and a null category filtered every row out
/// of the list. Neither was visible from a compile.
/// </summary>
public sealed class MainWindowViewModelTests
{
    private static FakeBackend Catalog() => new()
    {
        Statuses =
        {
            FakeBackend.Tweak("perf.measured", TweakCategories.Performance, evidence: Evidence.Measured, applied: true, managed: true),
            FakeBackend.Tweak("perf.plausible", TweakCategories.Performance),
            FakeBackend.Tweak("background.one", TweakCategories.Background),
            FakeBackend.Tweak("background.two", TweakCategories.Background),
            FakeBackend.Tweak("ping.one", TweakCategories.Ping),
        },
    };

    /// <summary>Adds a row that cannot run here, and reports itself as applied anyway.</summary>
    private static FakeBackend WithUnavailable()
    {
        var backend = Catalog();
        backend.Statuses.Add(FakeBackend.Tweak(
            "background.absent", TweakCategories.Background, applied: true, applicable: false));
        return backend;
    }

    private static async Task<MainWindowViewModel> LoadedAsync(FakeBackend? backend = null)
    {
        var viewModel = new MainWindowViewModel(backend ?? Catalog());
        await viewModel.InitialiseAsync();
        return viewModel;
    }

    [Fact]
    public async Task Every_tweak_in_the_catalog_is_listed()
    {
        // Poorly evidenced entries used to be filtered out until a checkbox was ticked. Someone
        // who had heard of one and came looking found nothing, and the only conclusion
        // available to them was that the tool did not have it.
        var viewModel = await LoadedAsync();

        Assert.Equal(5, viewModel.Tweaks.Count);
        Assert.Equal(4, viewModel.Tweaks.Count(t => t.Evidence == Evidence.Plausible));
    }

    [Fact]
    public async Task A_tweak_that_cannot_run_here_reads_OFF_even_if_it_reports_itself_applied()
    {
        // Applicability and state are answered by two independent methods on ITweak, so they
        // can disagree: a service that exists but starts at Boot scope is refused as a target
        // while still reading as applied. "ON" beside "not applicable here" is a contradiction,
        // and the reader resolves it by distrusting the badge.
        var viewModel = await LoadedAsync(WithUnavailable());

        var row = viewModel.Tweaks.Single(t => t.Id == "background.absent");

        Assert.True(row.IsApplied);
        Assert.False(row.ShowsAsApplied);
        Assert.Equal("OFF", row.StateLabel);
    }

    [Fact]
    public async Task Unavailable_tweaks_sink_to_the_bottom_under_their_own_band()
    {
        var viewModel = await LoadedAsync(WithUnavailable());

        Assert.Equal("background.absent", viewModel.Tweaks[^1].Id);
        Assert.Equal(MainWindowViewModel.NotApplicableHeader, viewModel.Tweaks[^1].GroupHeader);
        Assert.All(viewModel.Tweaks.SkipLast(1), t => Assert.True(t.IsApplicable));
    }

    [Fact]
    public async Task The_unavailable_filter_is_offered_only_when_something_is_unavailable()
    {
        // A filter that is usually empty trains people to ignore it, and on a machine where
        // everything applies it is a question with no answer.
        var withNone = await LoadedAsync();
        Assert.DoesNotContain(MainWindowViewModel.NotApplicableCategory, withNone.Categories);

        var withSome = await LoadedAsync(WithUnavailable());
        Assert.Contains(MainWindowViewModel.NotApplicableCategory, withSome.Categories);
    }

    [Fact]
    public async Task Selecting_the_unavailable_filter_shows_exactly_those()
    {
        var viewModel = await LoadedAsync(WithUnavailable());

        viewModel.SelectedCategory = MainWindowViewModel.NotApplicableCategory;

        Assert.Equal(["background.absent"], viewModel.Tweaks.Select(t => t.Id));
        Assert.NotNull(viewModel.SelectedCategoryPromise);
    }

    [Fact]
    public async Task An_unavailable_row_sinks_within_a_category_filter_too()
    {
        // Otherwise picking a category puts something unclickable back in the middle of the
        // list, which is the behaviour the sinking was meant to remove.
        var backend = WithUnavailable();
        backend.Statuses.Add(FakeBackend.Tweak("background.three", TweakCategories.Background));
        var viewModel = await LoadedAsync(backend);

        viewModel.SelectedCategory = TweakCategories.Background;

        Assert.Equal("background.absent", viewModel.Tweaks[^1].Id);
    }

    [Fact]
    public async Task The_gaming_half_is_listed_before_the_windows_half()
    {
        // The whole point of the grouping. Sorted by category order alone, Background & Cleanup
        // would interleave with Ping and the reader would have no way to tell which rows claim
        // to do anything for a game.
        var viewModel = await LoadedAsync();

        var groups = viewModel.Tweaks.Select(t => TweakCategories.GroupOf(t.Category)).ToList();

        Assert.Equal(groups.OrderBy(g => g), groups);
    }

    [Fact]
    public async Task Only_the_first_row_of_a_group_carries_its_heading()
    {
        var viewModel = await LoadedAsync();

        var headed = viewModel.Tweaks.Where(t => t.HasGroupHeader).Select(t => t.GroupHeader).ToList();

        Assert.Equal(["Gaming", "Windows"], headed);
    }

    [Fact]
    public async Task Selecting_a_category_filters_the_list()
    {
        var viewModel = await LoadedAsync();

        viewModel.SelectedCategory = TweakCategories.Background;

        Assert.Equal(
            ["background.one", "background.two"],
            viewModel.Tweaks.Select(t => t.Id).Order());
    }

    [Fact]
    public async Task A_null_category_falls_back_to_all_rather_than_hiding_everything()
    {
        // A bound ListBox reports null whenever its items are rebuilt. Treating that as a
        // filter value emptied the entire catalog.
        var viewModel = await LoadedAsync();

        viewModel.SelectedCategory = null!;

        Assert.Equal(MainWindowViewModel.AllCategories, viewModel.SelectedCategory);
        Assert.Equal(5, viewModel.Tweaks.Count);
    }

    [Fact]
    public async Task Categories_are_derived_from_the_catalog_in_the_order_a_player_cares()
    {
        var viewModel = await LoadedAsync();

        // Not alphabetical, and the Gaming categories come first. Sorted A-Z the sidebar would
        // open on "Background & Cleanup", which is the least interesting thing the tool does.
        Assert.Equal(
            [
                MainWindowViewModel.AllCategories,
                TweakCategories.Performance,
                TweakCategories.Ping,
                TweakCategories.Background,
            ],
            viewModel.Categories);
    }

    [Fact]
    public async Task The_selected_category_explains_what_it_claims()
    {
        var viewModel = await LoadedAsync();

        Assert.Null(viewModel.SelectedCategoryPromise);

        viewModel.SelectedCategory = TweakCategories.Ping;

        Assert.Equal(TweakCategories.Get(TweakCategories.Ping).Promise, viewModel.SelectedCategoryPromise);
    }

    [Fact]
    public async Task Search_finds_a_category_by_a_word_no_tweak_uses()
    {
        // "hitching" appears in no id, title or summary. Without the category synonyms the
        // search box answers a reasonable question with an empty list.
        var viewModel = await LoadedAsync();

        viewModel.SearchText = "hitching";

        Assert.Equal(2, viewModel.Tweaks.Count);
        Assert.All(viewModel.Tweaks, t => Assert.Equal(TweakCategories.Performance, t.Category));
    }

    [Fact]
    public async Task Refreshing_does_not_disturb_the_selected_category()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        viewModel.SelectedCategory = TweakCategories.Background;

        viewModel.RefreshCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(TweakCategories.Background, viewModel.SelectedCategory);
        Assert.Equal(2, viewModel.Tweaks.Count);
    }

    [Fact]
    public async Task Search_matches_id_title_and_summary()
    {
        var backend = new FakeBackend
        {
            Statuses =
            {
                FakeBackend.Tweak("a.one", title: "Reduce latency", summary: "nothing"),
                FakeBackend.Tweak("b.two", title: "Something", summary: "improves latency"),
                FakeBackend.Tweak("c.three", title: "Unrelated", summary: "nothing"),
            },
        };
        var viewModel = await LoadedAsync(backend);

        viewModel.SearchText = "latency";

        Assert.Equal(2, viewModel.Tweaks.Count);
    }

    [Fact]
    public async Task Search_and_category_filters_combine()
    {
        var viewModel = await LoadedAsync();

        viewModel.SelectedCategory = TweakCategories.Performance;
        viewModel.SearchText = "measured";

        Assert.Single(viewModel.Tweaks);
        Assert.Equal("perf.measured", viewModel.Tweaks[0].Id);
    }

    [Fact]
    public async Task Outstanding_count_reflects_what_the_app_manages()
    {
        var viewModel = await LoadedAsync();

        Assert.Equal(1, viewModel.OutstandingCount);
        Assert.Contains("1 change", viewModel.OutstandingText);
    }

    [Fact]
    public async Task A_selection_that_the_filter_removes_is_cleared()
    {
        // Otherwise the detail panel keeps describing a tweak that is no longer in the list.
        var viewModel = await LoadedAsync();
        viewModel.SelectedTweak = viewModel.Tweaks.First(t => t.Category == TweakCategories.Background);

        viewModel.SelectedCategory = TweakCategories.Performance;

        Assert.Null(viewModel.SelectedTweak);
        Assert.False(viewModel.HasSelection);
    }

    [Fact]
    public async Task Falling_back_to_the_local_engine_is_reported_not_hidden()
    {
        var backend = new FakeBackend { Description = "direct, not elevated", CanApplyMachineScope = false };

        var viewModel = await LoadedAsync(backend);

        Assert.False(viewModel.IsServiceMode);
        Assert.True(viewModel.ShowElevationWarning);
        Assert.Equal("direct, not elevated", viewModel.ConnectionText);
    }

    // ------------------------------------------------------------------ choices

    private static TweakChoice Level() => new()
    {
        Id = "level",
        Title = "Level",
        Description = "How hard to push it.",
        DefaultOption = "balanced",
        Options =
        [
            new TweakChoiceOption
            {
                Id = "balanced", Title = "Balanced", Description = "The safe one.", Recommended = true,
            },
            new TweakChoiceOption { Id = "max", Title = "Maximum", Description = "The loud one." },
        ],
    };

    [Fact]
    public async Task A_tweak_with_choices_starts_on_its_declared_default()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.tweak", choices: [Level()]));

        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single();

        var choice = Assert.Single(tweak.Choices);
        Assert.Equal("balanced", choice.Selected.Id);
        Assert.Equal(2, choice.Options.Count);

        // The descriptions have to reach the UI: without them this is a list of bare words.
        Assert.Equal("The safe one.", choice.Options[0].Description);
        Assert.True(choice.Options[0].Recommended);
    }

    [Fact]
    public async Task Applying_a_tweak_sends_the_selected_options()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.tweak", choices: [Level()]));

        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single();

        tweak.Choices[0].Options[1].IsChecked = true;
        await viewModel.ApplyCommand.ExecuteAsync(tweak);

        var (id, options) = Assert.Single(backend.Applies);
        Assert.Equal("a.tweak", id);
        Assert.NotNull(options);
        Assert.Equal("max", options["level"]);
    }

    [Fact]
    public async Task Choosing_a_different_option_re_reads_that_tweak()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.tweak", applied: false, choices: [Level()]));

        // Under "max" the machine already matches, so the badge has to flip to ON without the
        // user applying anything.
        backend.StatusBySelection["a.tweak:max"] =
            FakeBackend.Tweak("a.tweak", applied: true, choices: [Level()]);

        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single();
        Assert.False(tweak.IsApplied);

        tweak.Choices[0].Options[1].IsChecked = true;
        await Task.Delay(50);

        Assert.True(tweak.IsApplied);
        Assert.Contains(backend.StatusReads, r => r.Options is not null && r.Options["level"] == "max");
    }

    [Fact]
    public async Task A_selection_survives_a_catalog_refresh()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.tweak", choices: [Level()]));

        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single();
        tweak.Choices[0].Options[1].IsChecked = true;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Rebuilding the choice view models on every refresh would silently reset this to the
        // default, and the next Apply would do something the user did not ask for.
        Assert.Equal("max", viewModel.Tweaks.Single().Choices[0].Selected.Id);
    }

    [Fact]
    public async Task A_dry_run_does_not_reach_the_apply_path()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks[0];

        viewModel.DryRunCommand.Execute(tweak);
        await Task.Delay(50);

        Assert.Empty(backend.Applied);
    }

    [Fact]
    public async Task Revert_all_is_only_offered_when_something_is_outstanding()
    {
        var nothingManaged = new FakeBackend
        {
            Statuses = { FakeBackend.Tweak("a.one", managed: false) },
        };

        Assert.False((await LoadedAsync(nothingManaged)).RevertAllCommand.CanExecute(null));
        Assert.True((await LoadedAsync()).RevertAllCommand.CanExecute(null));
    }
}
