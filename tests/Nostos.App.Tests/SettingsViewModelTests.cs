using Nostos.App.Startup;
using Nostos.App.ViewModels;
using Nostos.Core.Settings;
using Nostos.Win32.Removal;

namespace Nostos.App.Tests;

/// <summary>
/// The settings panel: update preferences, and removing the app from the machine.
///
/// The removal half is the reason this file exists. It is the one path in the program that
/// cannot be tried out on a real machine twice, so the order it does things in -- undo, then
/// disconnect, then delete -- is asserted here rather than discovered by somebody whose PC is
/// mid-uninstall.
/// </summary>
public sealed class SettingsViewModelTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Settings { get; set; } = new();
        public int Saves { get; private set; }
        public bool Fails { get; set; }

        public AppSettings Load() => Settings;

        public bool Save(AppSettings settings)
        {
            if (Fails)
                return false;

            Settings = settings;
            Saves++;
            return true;
        }
    }

    private sealed class FakeHost : ISettingsHost
    {
        public List<string> Calls { get; } = [];
        public int OutstandingCount { get; set; }
        public int RevertResult { get; set; } = 3;
        public string CheckResult { get; set; } = "You are running the newest version (1.2.3).";

        public Task<string> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            Calls.Add("check");
            return Task.FromResult(CheckResult);
        }

        public Task<int> RevertEverythingAsync(CancellationToken ct = default)
        {
            Calls.Add("revert");
            return Task.FromResult(RevertResult);
        }

        public Task DetachAsync()
        {
            Calls.Add("detach");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRemover : ISystemRemover
    {
        public List<string> Calls { get; } = [];

        public RemovalTargets Targets { get; set; } = new(
            ServiceInstalled: true,
            DataRoot: @"C:\ProgramData\Nostos",
            IsPortable: false,
            LocalState: null,
            InstallDirectory: @"C:\Games\Nostos",
            ExecutablePath: @"C:\Games\Nostos\Nostos.exe",
            SingleFile: false,
            Helper: @"C:\Games\Nostos\Nostos.Service.exe");

        public RemovalResult Result { get; set; } = new(
            Completed: true,
            Done: ["Stopped and removed the background service", @"Deleted C:\ProgramData\Nostos"],
            Leftovers: [],
            DeleteByHand: [@"C:\Games\Nostos"]);

        public RemovalTargets Inspect()
        {
            Calls.Add("inspect");
            return Targets;
        }

        public Task<RemovalResult> RemoveAsync(CancellationToken ct = default)
        {
            Calls.Add("remove");
            return Task.FromResult(Result);
        }
    }

    private static (SettingsViewModel Settings, FakeHost Host, FakeRemover Remover, MemoryStore Store)
        Build(int outstanding = 4, AppSettings? initial = null)
    {
        var host = new FakeHost { OutstandingCount = outstanding };
        var remover = new FakeRemover();
        var store = new MemoryStore { Settings = initial ?? new AppSettings() };

        return (new SettingsViewModel(host, remover, store), host, remover, store);
    }

    // ------------------------------------------------------------------ updates

    [Fact]
    public void Turning_checking_off_is_written_down_immediately()
    {
        var (settings, _, _, store) = Build();

        settings.CheckForUpdates = false;

        Assert.False(store.Settings.CheckForUpdates);
        Assert.False(settings.CadenceEnabled);
    }

    [Fact]
    public void Choosing_a_cadence_is_written_down_immediately()
    {
        var (settings, _, _, store) = Build();

        settings.SelectedCadence = settings.Cadences.Single(c => c.Value == UpdateCadence.Weekly);

        Assert.Equal(UpdateCadence.Weekly, store.Settings.Cadence);
        Assert.Contains("once a week", settings.UpdatePolicyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_null_selection_from_a_rebuilding_combo_box_changes_nothing()
    {
        // Same failure the category list had: a control that reports null while its items are
        // being rebuilt would otherwise write a preference the user never chose.
        var (settings, _, _, store) = Build(initial: new AppSettings { Cadence = UpdateCadence.Daily });

        settings.SelectedCadence = null!;

        Assert.Equal(UpdateCadence.Daily, store.Settings.Cadence);
    }

    [Fact]
    public void Settings_that_cannot_be_saved_say_so_instead_of_pretending()
    {
        var (settings, _, _, store) = Build();
        store.Fails = true;

        settings.CheckForUpdates = false;

        Assert.True(settings.HasSaveProblem);
    }

    [Fact]
    public async Task Checking_now_reports_what_it_found_and_records_the_attempt()
    {
        var (settings, host, _, store) = Build();

        await settings.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(["check"], host.Calls);
        Assert.Equal(host.CheckResult, settings.CheckResult);
        Assert.NotNull(store.Settings.LastCheckedUtc);
    }

    // ------------------------------------------------------------------ removal

    [Fact]
    public void The_plan_names_everything_that_will_go()
    {
        var (settings, _, _, _) = Build(outstanding: 4);

        settings.PrepareRemoval();

        Assert.Contains(settings.RemovalPlan, line => line.Contains("4 changes"));
        Assert.Contains(settings.RemovalPlan, line => line.Contains("background service"));
        Assert.Contains(settings.RemovalPlan, line => line.Contains(@"C:\ProgramData\Nostos"));
        Assert.Contains(settings.RemovalPlan, line => line.Contains(@"C:\Games\Nostos"));
    }

    [Fact]
    public void A_machine_with_nothing_applied_is_not_offered_a_revert()
    {
        var (settings, _, _, _) = Build(outstanding: 0);

        settings.PrepareRemoval();

        Assert.False(settings.HasChangesToRevert);
        Assert.DoesNotContain(settings.RemovalPlan, line => line.Contains("Undo"));
    }

    [Fact]
    public async Task Removal_takes_two_clicks()
    {
        var (settings, _, remover, _) = Build();

        await settings.ArmRemovalCommand.ExecuteAsync(null);

        Assert.True(settings.RemovalArmed);
        Assert.True(settings.ShowRemovalConfirmation);
        Assert.DoesNotContain("remove", remover.Calls);
    }

    [Fact]
    public async Task Cancelling_the_confirmation_leaves_the_machine_alone()
    {
        var (settings, host, remover, _) = Build();

        await settings.ArmRemovalCommand.ExecuteAsync(null);
        await settings.CancelRemovalCommand.ExecuteAsync(null);

        Assert.False(settings.RemovalArmed);
        Assert.True(settings.ShowRemoveButton);
        Assert.Empty(host.Calls);
        Assert.DoesNotContain("remove", remover.Calls);
    }

    [Fact]
    public async Task Removal_undoes_the_changes_before_it_disconnects_or_deletes_anything()
    {
        // The order is the whole design. Reverting needs the service and the journal, and both
        // are about to stop existing; doing this the other way round would leave a machine with
        // every tweak still applied and nothing left that knows what they were.
        var (settings, host, remover, _) = Build();

        await settings.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(["revert", "detach"], host.Calls);
        Assert.Equal(["remove"], remover.Calls);
    }

    [Fact]
    public async Task Unticking_the_box_keeps_the_changes_and_still_removes_the_app()
    {
        var (settings, host, remover, _) = Build();
        settings.RevertChanges = false;

        await settings.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(["detach"], host.Calls);
        Assert.Contains("remove", remover.Calls);
        Assert.Contains("LEFT IN PLACE", settings.RemovalWarning);
    }

    [Fact]
    public async Task A_clean_removal_reports_what_went_and_what_is_left_to_delete()
    {
        var (settings, _, _, _) = Build();
        settings.PrepareRemoval();

        await settings.RemoveCommand.ExecuteAsync(null);

        Assert.True(settings.RemovalFinished);
        Assert.False(settings.HasRemovalProblem);
        Assert.False(settings.HasLeftovers);
        // Three, not the four that were outstanding: the number reported is what the revert
        // actually undid, not what the catalog thought was applied before it ran.
        Assert.Contains("Undid 3 changes", settings.RemovalSteps);
        Assert.Contains(@"C:\Games\Nostos", settings.DeleteByHand);
    }

    [Fact]
    public async Task A_declined_administrator_prompt_is_reported_rather_than_swallowed()
    {
        var (settings, _, remover, _) = Build();
        remover.Result = new RemovalResult(
            Completed: false,
            Done: [],
            Leftovers: [],
            DeleteByHand: [],
            Problem: "Removal needs administrator approval to delete the background service.");

        await settings.RemoveCommand.ExecuteAsync(null);

        Assert.True(settings.HasRemovalProblem);
        Assert.Contains("did not finish", settings.RemovalStatus);
    }

    [Fact]
    public async Task Files_that_would_not_go_are_named()
    {
        var (settings, _, remover, _) = Build();
        remover.Result = new RemovalResult(
            Completed: false,
            Done: ["Stopped and removed the background service"],
            Leftovers: [@"C:\ProgramData\Nostos\logs\service-20260826.log"],
            DeleteByHand: [@"C:\Games\Nostos"]);

        await settings.RemoveCommand.ExecuteAsync(null);

        Assert.True(settings.HasLeftovers);
        Assert.Contains(@"C:\ProgramData\Nostos\logs\service-20260826.log", settings.Leftovers);
    }

    [Fact]
    public async Task Nothing_is_written_back_to_disk_after_the_app_has_been_removed()
    {
        // The panel is still on screen when removal finishes, and its controls still work. A
        // save here would re-create the data folder that was just deleted, and with it the
        // claim that nothing is left would become false while the user watched.
        var (settings, _, _, store) = Build();
        await settings.RemoveCommand.ExecuteAsync(null);
        var saves = store.Saves;

        settings.CheckForUpdates = false;

        Assert.Equal(saves, store.Saves);
    }

    [Fact]
    public async Task Closing_after_removal_asks_the_app_to_exit()
    {
        var (settings, _, _, _) = Build();
        var exits = 0;
        settings.Exit += () => exits++;

        await settings.CloseCommand.ExecuteAsync(null);

        Assert.Equal(1, exits);
    }
}
