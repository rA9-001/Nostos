using Nostos.App.ViewModels;
using Nostos.Core.Localization;
using Nostos.Win32.Apps;

namespace Nostos.App.Tests;

/// <summary>
/// The Apps tab: what is installed, and removing it.
///
/// Enumerating needs no privilege, so the window reads the machine in-process exactly as it does
/// for startup entries, and only the removal goes through a backend. What these hold is the part
/// that could quietly lie -- a row saying something was removed when it was not.
/// </summary>
public sealed class AppsTabTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static InstalledApp Fake(
        string id = "machine:Widget",
        string name = "Contoso Widget",
        AppSource source = AppSource.Machine,
        long size = 0,
        string? quiet = null)
        => new(id, name, source, Publisher: "Contoso", Version: "1.2.3",
            QuietUninstallCommand: quiet, SizeBytes: size);

    private static AppItemViewModel Row(InstalledApp app, Func<AppItemViewModel, Task>? remove = null)
        => new(app, remove ?? (_ => Task.CompletedTask));

    [Fact]
    public void A_row_shows_the_publisher_and_version_under_the_name()
    {
        var row = Row(Fake());

        Assert.Equal("Contoso Widget", row.Name);
        Assert.Equal("Contoso  ·  1.2.3", row.Subtitle);
    }

    [Fact]
    public void A_size_Windows_does_not_know_is_left_blank_rather_than_shown_as_zero()
    {
        // EstimatedSize is absent more often than present and wrong a fair share of the time it
        // is there. An empty cell is honest; "0 MB" is a claim.
        Assert.Equal("", Row(Fake()).SizeText);
        Assert.Equal("512 MB", Row(Fake(size: 512L * 1024 * 1024)).SizeText);
        Assert.Equal("1.5 GB", Row(Fake(size: (long)(1.5 * 1024 * 1024 * 1024))).SizeText);
    }

    [Fact]
    public void The_row_says_up_front_whether_removing_it_will_ask_questions()
    {
        // An installer that opens a wizard over the window is a worse surprise than a row that
        // warned about it.
        Assert.False(Row(Fake()).IsSilent);
        Assert.True(Row(Fake(quiet: "unins.exe /S")).IsSilent);
    }

    [Fact]
    public void The_source_is_named_in_the_readers_language()
    {
        var row = Row(Fake(source: AppSource.Store));

        Assert.Equal("Microsoft Store", row.SourceText);

        Strings.Language = Language.German;
        row.RefreshText();
        Assert.Equal("Microsoft Store", row.SourceText);

        var user = Row(Fake(source: AppSource.User));
        Assert.Equal("Dieses Konto", user.SourceText);
    }

    [Fact]
    public async Task A_row_cannot_be_removed_twice_while_the_first_removal_is_running()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;

        var row = Row(Fake(), _ =>
        {
            calls++;
            return gate.Task;
        });

        var first = row.RemoveAsync();
        row.State = RemovalRowState.Uninstalling;

        await row.RemoveAsync();
        Assert.Equal(1, calls);

        gate.SetResult();
        await first;
    }

    [Fact]
    public void A_row_being_worked_on_shows_a_spinner_rather_than_a_button()
    {
        var row = Row(Fake());

        Assert.True(row.IsInteractive);
        Assert.False(row.IsWorking);

        row.State = RemovalRowState.Sweeping;

        Assert.False(row.IsInteractive);
        Assert.True(row.IsWorking);
    }

    [Fact]
    public void A_failed_removal_can_be_tried_again_but_a_finished_one_cannot()
    {
        Assert.True(Row(Fake()) is { State: RemovalRowState.Idle, IsInteractive: true });

        var failed = Row(Fake());
        failed.State = RemovalRowState.Failed;
        Assert.True(failed.IsInteractive);

        var removed = Row(Fake());
        removed.State = RemovalRowState.Removed;
        Assert.False(removed.IsInteractive);
    }

    [Theory]
    [InlineData("Epic Games Launcher", "EG")]
    [InlineData("Discord", "DI")]
    [InlineData("Battle.net", "BN")]
    [InlineData("7-Zip", "7Z")]
    [InlineData("R", "R")]
    public void Every_row_has_something_to_draw_even_with_no_icon(string name, string monogram)
    {
        // A list where a third of the rows are blank squares reads as broken, and "ships no
        // artwork" is not a category anybody cares about. Two letters where the name is two
        // words, so a launcher is not just "E".
        Assert.Equal(monogram, Row(Fake(name: name)).Monogram);
    }

    [Fact]
    public void A_monogram_keeps_its_colour_between_launches()
    {
        // Derived from the name rather than from a position in the list, which is what makes it
        // useful: the same program is the same colour every time, so it can be found by shape.
        Assert.Equal(
            Row(Fake(name: "Discord")).MonogramBrushKey,
            Row(Fake(name: "Discord")).MonogramBrushKey);

        // And a hash that does not move when the runtime's string hashing does.
        Assert.Equal(AppItemViewModel.Hash("Discord"), AppItemViewModel.Hash("discord"));
        Assert.NotEqual(AppItemViewModel.Hash("Discord"), AppItemViewModel.Hash("Steam"));
    }

    [Fact]
    public async Task The_page_says_where_the_registry_backups_go()
    {
        // "Registry keys are exported before they are removed" is a promise, and a promise about
        // a file is worth much less if the reader cannot see where the file went.
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        Assert.Contains(AppRemoval.BackupDirectory, window.AppsBackupPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_blurb_carries_the_total_so_the_count_is_stated_once()
    {
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        Assert.Contains(window.Apps.Count.ToString(), window.AppsBlurb, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_window_lists_what_is_on_the_machine_without_asking_the_backend()
    {
        // Reading needs no privilege, so it is done in-process. That is why the tab works before
        // the service is installed, and why nothing here is stubbed.
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);

        await window.InitialiseAsync();

        Assert.NotEmpty(window.Apps);
        Assert.Empty(backend.AppsRemoved);
    }

    [Fact]
    public async Task Searching_narrows_the_list_and_the_count_says_so()
    {
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        var total = window.Apps.Count;
        var first = window.Apps[0].Name;

        window.AppSearch = first;

        Assert.True(window.Apps.Count <= total);
        Assert.Contains(window.Apps, a => a.Name == first);
        Assert.Contains(window.Apps.Count.ToString(), window.AppsSummary, StringComparison.Ordinal);
        Assert.Contains(total.ToString(), window.AppsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_search_puts_everything_back()
    {
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        var total = window.Apps.Count;
        window.AppSearch = "something that matches nothing at all";
        Assert.Empty(window.Apps);

        window.AppSearch = "";
        Assert.Equal(total, window.Apps.Count);
    }

    [Theory]
    [InlineData(RemovalStage.Uninstalling, RemovalRowState.Uninstalling)]
    [InlineData(RemovalStage.Scanning, RemovalRowState.Scanning)]
    [InlineData(RemovalStage.Sweeping, RemovalRowState.Sweeping)]
    public void Each_stage_of_a_removal_puts_the_row_into_the_matching_state(
        RemovalStage stage, RemovalRowState expected)
        => Assert.Equal(expected, AppItemViewModel.StateFor(stage, RemovalRowState.Idle));

    [Fact]
    public void Finishing_leaves_the_row_alone_until_the_report_is_read()
    {
        // What the row says next depends on the report, which the window has and the row does
        // not. Leaving it keeps the spinner up for the moment between the last sweep and the
        // summary, rather than flashing back to Idle and then to Removed.
        Assert.Equal(
            RemovalRowState.Sweeping,
            AppItemViewModel.StateFor(RemovalStage.Finished, RemovalRowState.Sweeping));
    }

    [Fact]
    public async Task A_removal_goes_to_the_backend_and_leaves_the_row_finished()
    {
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await window.RemoveAppCommand.ExecuteAsync(row);

        Assert.Equal([row.Id], backend.AppsRemoved);
        Assert.Equal(RemovalRowState.Removed, row.State);
    }

    [Fact]
    public async Task The_machine_is_only_read_for_a_row_somebody_asked_about()
    {
        // The leftover scan measures folder sizes. Doing that for a hundred rows to fill a panel
        // nobody has opened would make the tab slow to earn nothing, so it happens on demand.
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        var row = window.Apps[0];
        Assert.False(row.IsOpen);
        Assert.False(row.HasInspection);

        await row.ToggleDetailsAsync();

        Assert.True(row.IsOpen);
        Assert.True(row.HasInspection);
        Assert.NotEmpty(row.RegistrationText);
        Assert.NotEmpty(row.WhyText);

        // Closing keeps what was read, so opening it again is instant.
        await row.ToggleDetailsAsync();
        Assert.False(row.IsOpen);
        Assert.True(row.HasInspection);
    }

    [Fact]
    public async Task The_panel_names_the_thing_that_puts_the_row_in_the_list()
    {
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await row.ToggleDetailsAsync();

        // Whatever the source, the first thing shown is the registration itself, and the row's
        // own id has to appear in it -- an explanation that does not identify the entry is not
        // an explanation.
        Assert.Contains(
            row.Evidence,
            e => e.Path == row.RegistrationText);
    }

    [Fact]
    public async Task One_thing_can_be_removed_without_the_others()
    {
        // "Delete only the registry key, leave the folder" is a real thing to want, and the
        // panel is where somebody looking at the list of what an entry owns decides that.
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await row.ToggleDetailsAsync();

        if (row.Leftovers.Count == 0)
            return;

        var item = row.Leftovers[0];
        await window.RemoveItemCommand.ExecuteAsync(item);

        Assert.Equal([(row.Id, item.Path)], backend.ItemsRemoved);

        // And it is not the whole-program removal wearing a different hat.
        Assert.Empty(backend.AppsRemoved);
        Assert.Empty(backend.AppsForgotten);
    }

    [Fact]
    public void Everything_a_state_change_affects_is_announced()
    {
        // The half that was missing, and the reason a correct sentence never reached the screen.
        // The line's visibility binds to HasStatus, and the state setter announced that the text
        // had changed without announcing that there was now a text to show -- so the row dimmed
        // and said nothing, which is what a button that did nothing looks like.
        //
        // Asserted against the list the setter actually iterates, because PropertyChanged is
        // marshalled to a UI thread and no test has one running: an omission is invisible to
        // every other test here. Anything on the row that reads the state belongs in this list.
        Assert.Contains(nameof(AppItemViewModel.HasStatus), AppItemViewModel.StateDependent);
        Assert.Contains(nameof(AppItemViewModel.StatusText), AppItemViewModel.StateDependent);
        Assert.Contains(nameof(AppItemViewModel.IsInteractive), AppItemViewModel.StateDependent);
        Assert.Contains(nameof(AppItemViewModel.IsWorking), AppItemViewModel.StateDependent);
        Assert.Contains(nameof(AppItemViewModel.RowOpacity), AppItemViewModel.StateDependent);

        // And they are real properties, so a rename cannot leave a string behind that no longer
        // names anything.
        Assert.All(
            AppItemViewModel.StateDependent,
            name => Assert.NotNull(typeof(AppItemViewModel).GetProperty(name)));
    }

    [Fact]
    public async Task Removing_the_entry_says_on_the_row_that_it_went()
    {
        // The bug this exists for: the entry was removed from the registry and the window said
        // nothing at all. The tab's reload does not rebuild the installed list, so the row stayed
        // exactly as it was -- which is what a failure looks like too. Pressing a button and
        // getting no answer is worse than an error, because the only way left to find out what
        // happened is regedit.
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await row.ToggleDetailsAsync();

        var entry = row.Leftovers.FirstOrDefault(l => l.IsRegistration);
        if (entry is null)
            return;

        await window.RemoveItemCommand.ExecuteAsync(entry);

        Assert.Equal(RemovalRowState.Removed, row.State);
        Assert.NotEmpty(row.StatusText);
        Assert.True(row.HasStatus);

        // And the panel closes rather than going on listing an entry that has gone: the
        // registration is built from the id, so a rebuild cannot notice it is no longer there.
        Assert.False(row.IsOpen);
    }

    [Fact]
    public async Task A_failed_item_removal_says_why_instead_of_looking_like_success()
    {
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await row.ToggleDetailsAsync();
        if (row.Leftovers.Count == 0)
            return;

        var item = row.Leftovers[0];
        backend.ItemResults[item.Path] = new RemovalReport(
            row.Id, row.Name, false, false, 0, 0, 0, [],
            ["the Recycle Bin would not take it"]);

        await window.RemoveItemCommand.ExecuteAsync(item);

        Assert.Equal(RemovalRowState.Failed, row.State);
        Assert.Contains("Recycle Bin", row.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_removable_thing_carries_the_row_it_belongs_to()
    {
        // The pairing is what the removal is checked against: the backend rebuilds what the
        // program owns from the live machine and refuses a path that is not on that list. A row
        // carrying a path alone would be a way to recycle anything on the machine.
        var window = new MainWindowViewModel(new FakeBackend());
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await row.ToggleDetailsAsync();

        Assert.All(row.Leftovers, l => Assert.Same(row, l.Owner));
        Assert.All(row.Leftovers, l => Assert.False(string.IsNullOrWhiteSpace(l.KindText)));
    }

    [Fact]
    public async Task Forgetting_an_entry_is_a_different_request_from_removing_a_program()
    {
        // Kept apart deliberately. Remove says "uninstall this"; forget says "the program is
        // gone, take its entry away too". A single button that quietly did whichever seemed to
        // apply would be guessing on the reader's behalf about the one thing they are deciding.
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await window.ForgetAppCommand.ExecuteAsync(row);

        Assert.Equal([row.Id], backend.AppsForgotten);
        Assert.Empty(backend.AppsRemoved);
    }

    [Fact]
    public async Task A_removal_that_did_not_finish_says_so_on_the_row()
    {
        // The important one. A row reading "Removed" over a program that is still installed is
        // the single outcome this tab must never produce -- and the failure it would come from
        // is real: a launcher that hands the uninstall to a process already running returns
        // success immediately, before the user has even been asked.
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        backend.RemovalResults[row.Id] = new RemovalReport(
            row.Id, row.Name, UninstallerRan: true, NeededTheUser: false,
            FoldersRecycled: 0, KeysRemoved: 0, BytesReclaimed: 0,
            Left: [],
            Problems: ["still listed as installed afterwards, so nothing was swept up"]);

        await window.RemoveAppCommand.ExecuteAsync(row);

        Assert.Equal(RemovalRowState.Failed, row.State);
        Assert.Contains("still listed", row.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_removal_says_what_it_swept_up()
    {
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        backend.RemovalResults[row.Id] = new RemovalReport(
            row.Id, row.Name, true, false, 3, 2, 1024L * 1024 * 700, [], []);

        await window.RemoveAppCommand.ExecuteAsync(row);

        Assert.Equal(RemovalRowState.Removed, row.State);
        Assert.Contains("3", row.StatusText, StringComparison.Ordinal);
        Assert.Contains("2", row.StatusText, StringComparison.Ordinal);
        Assert.Contains("700 MB", row.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_removal_that_left_nothing_behind_says_that_rather_than_listing_zeroes()
    {
        var backend = new FakeBackend();
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        var row = window.Apps[0];
        await window.RemoveAppCommand.ExecuteAsync(row);

        Assert.Equal("Removed. Nothing was left behind.", row.StatusText);
    }
}
