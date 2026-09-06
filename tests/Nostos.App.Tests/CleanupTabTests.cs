using Nostos.App.ViewModels;
using Nostos.Core.Localization;
using Nostos.Win32.Cleanup;

namespace Nostos.App.Tests;

/// <summary>
/// The Cleanup tab: what it says is there, and what it does about it.
/// </summary>
public sealed class CleanupTabTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static CleanupFinding Finding(
        string id = "shaders.nvidia-dx",
        long bytes = 4L * 1024 * 1024 * 1024,
        int files = 80,
        int held = 0,
        CleanupBlock blocked = CleanupBlock.None,
        CleanupGroup group = CleanupGroup.Shaders)
        => new(new CleanupTarget(id, group, @"C:\cache\" + id), bytes, files, held, blocked);

    private static async Task<MainWindowViewModel> Window(params CleanupFinding[] findings)
    {
        var backend = new FakeBackend();
        backend.CleanupFindings.AddRange(findings);

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        await window.ScanCleanupCommand.ExecuteAsync(null);

        return window;
    }

    [Fact]
    public async Task The_total_counts_only_what_can_actually_be_emptied()
    {
        // A blocked row is measured and shown -- "you would get this back if you ran elevated"
        // is worth knowing -- but promising it in the headline would be a number the button
        // cannot deliver.
        var window = await Window(
            Finding(bytes: 1024 * 1024),
            Finding(id: "temp.windows", bytes: 500L * 1024 * 1024, blocked: CleanupBlock.NeedsAdmin));

        Assert.Contains("1 MB", window.CleanupSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("500", window.CleanupSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_to_do_says_so_rather_than_showing_a_zero()
    {
        var window = await Window(Finding(bytes: 0, files: 0));

        Assert.False(window.CanClean);
        Assert.Equal(Strings.Get("clean.nothing"), window.CleanupSummary);
    }

    [Fact]
    public async Task Every_row_says_what_it_is_and_what_losing_it_costs()
    {
        var window = await Window(Finding());
        var row = window.Cleanup[0];

        Assert.Equal(Strings.Get("clean.name.shaders.nvidia-dx"), row.Name);
        Assert.NotEmpty(row.Cost);
        Assert.Equal("4 GB", row.SizeText);
    }

    [Fact]
    public async Task What_the_age_rule_held_back_is_on_the_row()
    {
        // Otherwise a size that looks far too small next to what Explorer reports has no
        // explanation anywhere near it.
        var window = await Window(Finding(id: "temp.user", files: 12136, held: 5519, group: CleanupGroup.Temp));

        Assert.Contains("12136", window.Cleanup[0].CountText.Replace(",", "").Replace(".", ""),
            StringComparison.Ordinal);
        Assert.Contains("5519", window.Cleanup[0].CountText.Replace(",", "").Replace(".", ""),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_that_needs_administrator_rights_keeps_its_button_and_says_so()
    {
        // The first version hid the button, which is how somebody learns that this program
        // cannot clean Windows' own temp folder rather than that their copy is not elevated.
        var window = await Window(Finding(blocked: CleanupBlock.NeedsAdmin));
        var row = window.Cleanup[0];

        Assert.True(row.NeedsAdmin);
        Assert.True(row.ShowsCleanButton);
        Assert.False(row.CanClean);
        Assert.Contains("administrator", row.CountText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pressing_it_anyway_says_what_to_do_and_touches_nothing()
    {
        var backend = new FakeBackend();
        backend.CleanupFindings.Add(Finding(id: "temp.windows", blocked: CleanupBlock.NeedsAdmin));

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        await window.ScanCleanupCommand.ExecuteAsync(null);

        var row = window.Cleanup[0];
        await window.CleanItemCommand.ExecuteAsync(row);

        Assert.Empty(backend.Cleaned);
        Assert.Equal(CleanupRowState.NeedsAdmin, row.State);
        Assert.True(row.HasStatus);
        Assert.Contains("administrator", row.StatusText, StringComparison.OrdinalIgnoreCase);

        // And it is still offering to try, because what would change the answer is starting the
        // program differently rather than anything on this page.
        Assert.True(row.ShowsCleanButton);
    }

    [Fact]
    public async Task What_an_elevated_launch_would_reach_is_stated_above_the_list()
    {
        var window = await Window(
            Finding(bytes: 1024 * 1024),
            Finding(id: "temp.windows", bytes: 2L * 1024 * 1024 * 1024,
                blocked: CleanupBlock.NeedsAdmin));

        Assert.True(window.HasCleanupAdminNote);
        Assert.Contains("2 GB", window.CleanupAdminNote, StringComparison.Ordinal);

        // The headline still promises only what the button can deliver.
        Assert.Contains("1 MB", window.CleanupSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_reachable_still_offers_the_button_when_something_needs_rights()
    {
        // A page whose only rows are the machine-wide ones has something to say, and a greyed
        // out button says none of it.
        var window = await Window(
            Finding(id: "temp.windows", bytes: 2L * 1024 * 1024 * 1024,
                blocked: CleanupBlock.NeedsAdmin));

        Assert.False(window.CanClean);
        Assert.True(window.CanCleanAnything);
    }

    [Fact]
    public async Task Cleaning_everything_accounts_for_the_rows_it_could_not_take()
    {
        var window = await Window(
            Finding(),
            Finding(id: "temp.windows", blocked: CleanupBlock.NeedsAdmin));

        await window.CleanAllCommand.ExecuteAsync(null);

        var blocked = window.Cleanup.Single(c => c.Id == "temp.windows");

        Assert.Equal(CleanupRowState.NeedsAdmin, blocked.State);
        Assert.Contains("administrator", blocked.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cleaning_a_row_asks_the_backend_for_that_target_and_no_other()
    {
        var backend = new FakeBackend();
        backend.CleanupFindings.Add(Finding());
        backend.CleanupFindings.Add(Finding(id: "launcher.discord", group: CleanupGroup.Launchers));

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        await window.ScanCleanupCommand.ExecuteAsync(null);

        await window.CleanItemCommand.ExecuteAsync(window.Cleanup[0]);

        Assert.Equal(["shaders.nvidia-dx"], backend.Cleaned);
    }

    [Fact]
    public async Task Cleaning_everything_takes_every_row_that_has_something_in_it()
    {
        var backend = new FakeBackend();
        backend.CleanupFindings.Add(Finding());
        backend.CleanupFindings.Add(Finding(id: "temp.windows", blocked: CleanupBlock.NeedsAdmin));
        backend.CleanupFindings.Add(Finding(id: "launcher.discord", bytes: 0, files: 0));

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        await window.ScanCleanupCommand.ExecuteAsync(null);

        await window.CleanAllCommand.ExecuteAsync(null);

        // Not the blocked one, and not the empty one.
        Assert.Equal(["shaders.nvidia-dx"], backend.Cleaned);
    }

    [Fact]
    public async Task A_finished_row_says_how_much_it_freed()
    {
        var backend = new FakeBackend();
        backend.CleanupFindings.Add(Finding());
        backend.CleanupResults["shaders.nvidia-dx"] =
            new CleanupResult("shaders.nvidia-dx", 4L * 1024 * 1024 * 1024, 80, 0, []);

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        await window.ScanCleanupCommand.ExecuteAsync(null);

        var row = window.Cleanup[0];
        await window.CleanItemCommand.ExecuteAsync(row);

        Assert.Equal(CleanupRowState.Cleaned, row.State);
        Assert.True(row.HasStatus);
        Assert.Contains("4 GB", row.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_that_could_not_be_emptied_says_why()
    {
        var backend = new FakeBackend();
        backend.CleanupFindings.Add(Finding());
        backend.CleanupResults["shaders.nvidia-dx"] =
            new CleanupResult("shaders.nvidia-dx", 0, 0, 0, ["needs an elevated launch"]);

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();
        await window.ScanCleanupCommand.ExecuteAsync(null);

        var row = window.Cleanup[0];
        await window.CleanItemCommand.ExecuteAsync(row);

        Assert.Equal(CleanupRowState.Failed, row.State);
        Assert.Contains("elevated", row.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Everything_a_state_change_affects_is_announced()
    {
        // The same lesson the Apps tab paid for: PropertyChanged is marshalled to a UI thread no
        // test runs, so a missing notification is invisible to the suite and shows up only as a
        // row that quietly says nothing after a button is pressed.
        Assert.Contains(nameof(CleanupItemViewModel.HasStatus), CleanupItemViewModel.StateDependent);
        Assert.Contains(nameof(CleanupItemViewModel.StatusText), CleanupItemViewModel.StateDependent);
        Assert.Contains(nameof(CleanupItemViewModel.CanClean), CleanupItemViewModel.StateDependent);

        Assert.All(
            CleanupItemViewModel.StateDependent,
            name => Assert.NotNull(typeof(CleanupItemViewModel).GetProperty(name)));
    }

    [Fact]
    public async Task The_page_names_what_it_will_never_touch()
    {
        // The promise is only a promise where somebody can read it, and the Steam download
        // folder is the one that matters most.
        var window = await Window(Finding());

        Assert.Contains("downloading", window.CleanupNever, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Downloads", window.CleanupNever, StringComparison.OrdinalIgnoreCase);
    }
}
