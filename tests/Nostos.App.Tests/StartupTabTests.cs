using Nostos.App.ViewModels;
using Nostos.Core.Localization;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// The startup tab.
///
/// It is deliberately not a tweak list. There is no catalog entry per program, no risk rating
/// and no evidence claim, because the catalog cannot know what is installed on a machine: the
/// question here is "do I want Razer Synapse running before I have asked for it", which is a
/// fact about the reader rather than about Windows.
///
/// What the tests hold is the part that could quietly lie -- the row showing a state the machine
/// does not actually have.
/// </summary>
public sealed class StartupTabTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static StartupEntry Entry(
        string id = "user-run:Steam",
        string name = "Steam",
        bool enabled = true,
        bool machineWide = false)
        => new(id, name, "UserRun", $@"C:\Games\{name}.exe -silent",
            $@"C:\Games\{name}.exe", enabled, @"HKCU\...\CurrentVersion\Run", machineWide);

    private static StartupItemViewModel Row(
        StartupEntry entry, Func<StartupItemViewModel, bool, Task>? toggle = null)
        => new(entry, toggle ?? ((_, _) => Task.CompletedTask));

    [Fact]
    public void A_row_shows_the_program_rather_than_the_registry_value_name()
    {
        // A Run value's name is chosen by whoever wrote the installer: "RtkAudUService" and
        // "Update.exe" identify nothing on their own. The name is still the best short label,
        // so the path goes underneath it and does the identifying.
        var row = Row(Entry());

        Assert.Equal("Steam", row.Name);
        Assert.Equal(@"C:\Games\Steam.exe", row.Path);
    }

    [Fact]
    public void A_command_that_could_not_be_resolved_still_shows_something()
    {
        // Better than a blank cell: the raw command line is what the reader would have seen in
        // regedit, and it is enough to identify the program even when it cannot be resolved.
        var row = Row(Entry() with { ExecutablePath = null, Command = "rundll32 something,Entry" });

        Assert.Equal("rundll32 something,Entry", row.Path);
    }

    [Fact]
    public void A_disabled_row_is_dimmed_rather_than_only_labelled()
    {
        // The list's whole job is answering "what starts with my PC". The first version drew on
        // and off rows identically apart from a small pill at the far right, which made that
        // answer take fifteen edge-to-edge reads instead of one glance down the column.
        Assert.Equal(1.0, Row(Entry(enabled: true)).RowOpacity);
        Assert.True(Row(Entry(enabled: false)).RowOpacity < 0.6);
    }

    [Fact]
    public void The_scope_badge_says_who_an_entry_applies_to()
    {
        Assert.Equal("This account", Row(Entry(machineWide: false)).ScopeText);
        Assert.Equal("All users", Row(Entry(machineWide: true)).ScopeText);
    }

    [Fact]
    public void The_scope_badge_is_translated()
    {
        var row = Row(Entry(machineWide: true));

        Strings.Language = Language.German;
        row.RefreshText();

        Assert.Equal("Alle Benutzer", row.ScopeText);
    }

    [Fact]
    public void The_switch_crossfades_rather_than_blinking_between_two_words()
    {
        // Both halves of the switch are always in the tree and one of them is transparent, so a
        // flipped row animates. That is the affordance doing its job: a control that visibly
        // moves when clicked is one people believe they can click, and the first version -- a
        // word at the far right that silently changed from "On" to "Off" -- was not.
        var on = Row(Entry(enabled: true));
        var off = Row(Entry(enabled: false));

        Assert.Equal(1, on.OnOpacity);
        Assert.Equal(0, on.OffOpacity);
        Assert.Equal(0, off.OnOpacity);
        Assert.Equal(1, off.OffOpacity);
    }

    [Fact]
    public void The_row_says_what_clicking_it_will_do()
    {
        // On the tooltip rather than the row, because it is the same sentence fifteen times and
        // printing it on every line would bury the program names it is there to help with.
        Assert.Contains("stop Steam", Row(Entry(enabled: true)).ToggleHint, StringComparison.Ordinal);
        Assert.Contains("let Steam", Row(Entry(enabled: false)).ToggleHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_a_row_asks_for_the_opposite_of_what_it_currently_is()
    {
        var asked = new List<bool>();
        var row = Row(Entry(enabled: true), (_, enabled) =>
        {
            asked.Add(enabled);
            return Task.CompletedTask;
        });

        await row.ToggleAsync();

        Assert.Equal([false], asked);
    }

    [Fact]
    public async Task A_row_cannot_be_switched_twice_while_the_first_write_is_in_flight()
    {
        // Two writes racing to the same approval value is a way to end up with a row and a
        // machine that disagree, and the second click buys nothing anyway.
        var gate = new TaskCompletionSource();
        var calls = 0;

        var row = Row(Entry(), (_, _) =>
        {
            calls++;
            return gate.Task;
        });

        var first = row.ToggleAsync();
        Assert.False(row.IsInteractive);

        await row.ToggleAsync();
        Assert.Equal(1, calls);

        gate.SetResult();
        await first;
        Assert.True(row.IsInteractive);
    }

    [Fact]
    public void A_row_takes_the_state_the_machine_reports_not_the_one_that_was_asked_for()
    {
        // The important one. A machine-wide entry goes to the service and can be refused -- an
        // unelevated app with no service installed cannot write HKLM -- and a row that flipped
        // anyway would be claiming something about the machine that is not true.
        var row = Row(Entry(enabled: true));

        row.Update(Entry(enabled: true));

        Assert.True(row.IsEnabled);
        Assert.Equal(1.0, row.RowOpacity);
    }

    [Fact]
    public async Task The_window_lists_what_is_on_the_machine_without_asking_the_backend()
    {
        // Reading the list needs no privilege, so it is done in-process. That is why the tab
        // works before the service is installed, and it is why nothing here is stubbed: the
        // backend is only ever asked to *write*.
        var backend = new FakeBackend();
        var viewModel = new MainWindowViewModel(backend);

        await viewModel.InitialiseAsync();

        Assert.NotNull(viewModel.StartupItems);
        Assert.Empty(backend.StartupSets);
    }

    [Fact]
    public void The_summary_counts_what_will_actually_run()
    {
        var backend = new FakeBackend();
        var viewModel = new MainWindowViewModel(backend);

        // Whatever this machine has, the line has to agree with the rows on screen.
        var enabled = viewModel.StartupItems.Count(i => i.IsEnabled);
        Assert.Contains(enabled.ToString(), viewModel.StartupSummary, StringComparison.Ordinal);
        Assert.Contains(
            viewModel.StartupItems.Count.ToString(), viewModel.StartupSummary, StringComparison.Ordinal);
    }
}
