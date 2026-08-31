using Nostos.App.Backends;
using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// Pointing a process-scoped tweak at a process.
///
/// "Prioritise a running game process" was unusable from the window for as long as the window
/// had no way to name one. It reported itself not applicable with "no target process specified"
/// — true, and useless — and the only way to use it at all was `nos apply process.game-tuning
/// --pid 1234`. The plumbing for a target existed the whole way from the IPC contract to the
/// engine; the window simply never filled it in.
///
/// The target is deliberately not carried in the options dictionary. Options are a tweak's own
/// choices, they go into the journal as the record of what was asked for, and a process id is
/// neither a choice nor worth keeping: it means nothing once the process exits.
/// </summary>
public sealed class TargetProcessTests
{
    private const string ProcessTweak = "process.game-tuning";

    private static FakeBackend Catalog()
    {
        var backend = new FakeBackend();

        backend.Statuses.Add(FakeBackend.Tweak("shell.plain", TweakCategories.Performance));
        backend.Statuses.Add(ProcessScoped(applicable: false));

        return backend;
    }

    private static TweakStatusSummary ProcessScoped(bool applicable)
    {
        var status = FakeBackend.Tweak(
            ProcessTweak, TweakCategories.Performance, applicable: applicable);

        return status with
        {
            Tweak = status.Tweak with { Scope = TweakScope.Process },
            NotApplicableReason = applicable ? null : "no target process specified",
        };
    }

    private static async Task<MainWindowViewModel> LoadedAsync(FakeBackend backend)
    {
        var viewModel = new MainWindowViewModel(backend);
        await viewModel.InitialiseAsync();
        return viewModel;
    }

    [Fact]
    public async Task Selecting_a_process_scoped_tweak_offers_a_picker()
    {
        var viewModel = await LoadedAsync(Catalog());

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == "shell.plain");
        Assert.False(viewModel.NeedsTargetProcess);

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);
        Assert.True(viewModel.NeedsTargetProcess);
    }

    [Fact]
    public async Task The_picker_is_filled_the_moment_the_row_is_selected()
    {
        // Filled on selection rather than once at startup: the interesting process is usually
        // one started after this window was opened. Asserted against the real machine, which
        // always has at least this test host's own windows... or does not, on a build agent
        // with no desktop, so this only asserts that asking did not throw.
        var viewModel = await LoadedAsync(Catalog());

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);

        Assert.NotNull(viewModel.Processes);
    }

    [Fact]
    public async Task Choosing_a_process_re_reads_the_tweak_against_it()
    {
        // What moves the row out of "not applicable": for this one, applicability is not a fact
        // about the machine, it is whether the question has been answered yet.
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);
        backend.Targets.Clear();

        viewModel.SelectedProcess = new RunningProcess(4321, "game", "A Game");

        await Task.Delay(50);

        var read = Assert.Single(backend.Targets, t => t.TweakId == ProcessTweak);
        Assert.Equal(4321, read.Target?.ProcessId);
        Assert.Equal("game", read.Target?.ProcessName);
    }

    [Fact]
    public async Task Applying_sends_the_chosen_process()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);

        viewModel.SelectedTweak = tweak;
        viewModel.SelectedProcess = new RunningProcess(99, "game", "A Game");
        backend.Targets.Clear();

        await viewModel.ApplyCommand.ExecuteAsync(tweak);

        Assert.Contains(backend.Targets, t => t.Target is { ProcessId: 99, ProcessName: "game" });
    }

    [Fact]
    public async Task A_tweak_that_takes_no_target_is_never_sent_one()
    {
        // The picker keeps its selection while the reader moves around the list, and a stray
        // pid attached to a registry tweak would end up in that tweak's journal entry.
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);
        viewModel.SelectedProcess = new RunningProcess(99, "game", "A Game");

        var plain = viewModel.Tweaks.Single(t => t.Id == "shell.plain");
        viewModel.SelectedTweak = plain;
        backend.Targets.Clear();

        await viewModel.ApplyCommand.ExecuteAsync(plain);

        Assert.All(backend.Targets.Where(t => t.TweakId == "shell.plain"), t => Assert.Null(t.Target));
    }

    [Fact]
    public async Task The_target_does_not_leak_into_the_options()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);

        viewModel.SelectedTweak = tweak;
        viewModel.SelectedProcess = new RunningProcess(1234, "game", "A Game");

        await viewModel.ApplyCommand.ExecuteAsync(tweak);

        Assert.All(
            backend.Applies.Where(a => a.TweakId == ProcessTweak),
            a => Assert.DoesNotContain(
                a.Options ?? new Dictionary<string, string>(),
                option => option.Value.Contains("1234", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_machine_scoped_tweak_can_ask_for_one_too()
    {
        // process.persistent-priority writes HKLM and outlives every process it affects, so it
        // is machine-scoped -- and it still has to be told which executable. Deciding from the
        // scope alone left it with a question it could not ask and an Apply that could never do
        // anything, which is exactly the state game-tuning was in before the picker existed.
        var backend = Catalog();
        var status = FakeBackend.Tweak("process.persistent-priority", TweakCategories.Performance);

        backend.Statuses.Add(status with
        {
            Tweak = status.Tweak with { Scope = TweakScope.Machine, TakesTargetProcess = true },
        });

        var viewModel = await LoadedAsync(backend);
        var tweak = viewModel.Tweaks.Single(t => t.Id == "process.persistent-priority");

        viewModel.SelectedTweak = tweak;
        Assert.True(viewModel.NeedsTargetProcess);

        viewModel.SelectedProcess = new RunningProcess(7, "cs2", "Counter-Strike 2");
        backend.Targets.Clear();

        await viewModel.ApplyCommand.ExecuteAsync(tweak);

        Assert.Contains(backend.Targets, t => t.Target is { ProcessName: "cs2" });
    }

    [Fact]
    public async Task The_picker_says_something_different_for_a_change_that_outlives_the_process()
    {
        // "This tweak acts on one running process and is undone when that process exits" is the
        // opposite of true for the permanent one, which is only using the process to spell an
        // executable's name.
        var backend = Catalog();
        var status = FakeBackend.Tweak("process.persistent-priority", TweakCategories.Performance);

        backend.Statuses.Add(status with
        {
            Tweak = status.Tweak with { Scope = TweakScope.Machine, TakesTargetProcess = true },
        });

        var viewModel = await LoadedAsync(backend);

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == ProcessTweak);
        var session = viewModel.TargetNote;

        viewModel.SelectedTweak = viewModel.Tweaks.Single(t => t.Id == "process.persistent-priority");
        var persistent = viewModel.TargetNote;

        Assert.NotEqual(session, persistent);
        Assert.Contains("exits", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every future launch", persistent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_listed_process_reads_as_something_a_person_can_recognise()
    {
        Assert.Equal(
            "A Game  —  game.exe (4321)",
            new RunningProcess(4321, "game", "A Game").Display);

        // No window title: still identifiable, and still selectable.
        Assert.Equal("svc.exe (7)", new RunningProcess(7, "svc", "").Display);
    }
}
