using Nostos.App.Localization;
using Nostos.App.ViewModels;
using Nostos.Core.Localization;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// The rule this file holds, across every tab that has one: <b>when the program knows in advance
/// that something cannot be done without administrator rights, the control stays and says so.</b>
///
/// Three wrong answers were in the program before this, one per tab, all of them the same
/// mistake wearing different clothes:
///
/// <list type="bullet">
/// <item>The Cleanup tab removed the button. What that teaches is that this program cannot
/// clean Windows' own temp folder -- not that this copy is not elevated.</item>
/// <item>The Tweaks tab offered Apply, sent it, and printed whatever the engine said when it
/// refused: a sentence written for somebody at a command line, after a round trip.</item>
/// <item>The Startup tab let the switch move, watched HKLM refuse the write, re-read the entry
/// and put the switch back. A switch that flicks and springs back is the visual language of a
/// broken control.</item>
/// </list>
///
/// <para>None of these were dangerous and all of them were dishonest, in the specific way this
/// program is meant not to be: the answer was known before the click and was not said.</para>
/// </summary>
public sealed class AdminNoticeTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static async Task<MainWindowViewModel> Window(bool machineScope, params TweakStatusSummary[] tweaks)
    {
        var backend = new FakeBackend { CanApplyMachineScope = machineScope };
        backend.Statuses.AddRange(tweaks);

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        return window;
    }

    // ------------------------------------------------------------------------------- tweaks

    [Fact]
    public async Task A_machine_wide_tweak_says_it_needs_rights_this_copy_does_not_have()
    {
        var window = await Window(machineScope: false, FakeBackend.Tweak("a.b"));
        var row = window.Tweaks[0];

        Assert.True(row.NeedsAdmin);
        Assert.NotEmpty(row.AdminNote);
    }

    [Fact]
    public async Task With_a_way_to_reach_the_machine_it_says_nothing()
    {
        var window = await Window(machineScope: true, FakeBackend.Tweak("a.b"));

        Assert.False(window.Tweaks[0].NeedsAdmin);
        Assert.Empty(window.Tweaks[0].AdminNote);
    }

    [Fact]
    public async Task A_tweak_that_needs_no_rights_is_never_marked()
    {
        // The error worth avoiding is the other one: a page where everything claims to need
        // administrator rights is a page that has stopped saying anything.
        var window = await Window(
            machineScope: false, FakeBackend.Tweak("a.b", requiresElevation: false));

        Assert.False(window.Tweaks[0].NeedsAdmin);
    }

    [Fact]
    public async Task Applying_one_is_refused_here_rather_than_at_the_far_end_of_the_pipe()
    {
        var backend = new FakeBackend { CanApplyMachineScope = false };
        backend.Statuses.Add(FakeBackend.Tweak("a.b"));

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        await window.ApplyCommand.ExecuteAsync(window.Tweaks[0]);

        Assert.Empty(backend.Applied);
        Assert.True(window.StatusIsError);
        Assert.Contains("administrator", window.StatusDetail ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reverting_one_is_refused_the_same_way()
    {
        var backend = new FakeBackend { CanApplyMachineScope = false };
        backend.Statuses.Add(FakeBackend.Tweak("a.b", applied: true));

        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        await window.RevertCommand.ExecuteAsync(window.Tweaks[0]);

        Assert.Empty(backend.Reverted);
    }

    [Fact]
    public void Everything_a_change_of_privilege_affects_is_announced()
    {
        // The lesson the Apps tab paid for, applied here before it could be paid for again:
        // PropertyChanged is marshalled to a UI thread no test runs, so an omission is
        // invisible to the suite and shows up only as a row that quietly says nothing.
        Assert.Contains(nameof(TweakItemViewModel.NeedsAdmin), TweakItemViewModel.PrivilegeDependent);
        Assert.Contains(nameof(TweakItemViewModel.AdminNote), TweakItemViewModel.PrivilegeDependent);

        Assert.All(
            TweakItemViewModel.PrivilegeDependent,
            name => Assert.NotNull(typeof(TweakItemViewModel).GetProperty(name)));
    }

    // ------------------------------------------------------------------------------ startup

    private static StartupItemViewModel Row(bool machineWide, bool machineScope)
        => new(
            new StartupEntry(
                "machine-run:Widget", "Widget", "MachineRun", @"C:\Widget\widget.exe",
                @"C:\Widget\widget.exe", true, @"HKLM\...\Run", machineWide),
            (_, _) => Task.CompletedTask)
        {
            MachineScopeAvailable = machineScope,
        };

    [Fact]
    public void A_machine_wide_startup_entry_says_what_its_switch_needs()
    {
        var row = Row(machineWide: true, machineScope: false);

        Assert.True(row.NeedsAdmin);
        Assert.NotEmpty(row.AdminNote);
    }

    [Fact]
    public void The_same_entry_says_nothing_once_the_service_is_there()
    {
        Assert.False(Row(machineWide: true, machineScope: true).NeedsAdmin);
    }

    [Fact]
    public void An_entry_that_is_only_yours_never_needs_them()
    {
        Assert.False(Row(machineWide: false, machineScope: false).NeedsAdmin);
    }

    // ----------------------------------------------------------------------------- profiles

    private static ProfileViewModel Profile(bool machineScope, params string[] ids)
    {
        var catalog = ids.ToDictionary(
            id => id,
            id => new TweakItemViewModel(FakeBackend.Tweak(id)) { MachineScopeAvailable = machineScope },
            StringComparer.OrdinalIgnoreCase);

        return new ProfileViewModel(
            new ProfileSummary("basic", "A profile.", ids.Length, ids),
            id => catalog.GetValueOrDefault(id));
    }

    [Fact]
    public void A_profile_says_how_much_of_it_would_be_skipped_before_it_is_applied()
    {
        // Pressing Apply used to answer this by marching through the rows marking most of them
        // skipped, under a summary that guessed between two causes: "not applicable to this PC,
        // or needs administrator".
        var profile = Profile(machineScope: false, "one", "two", "three");

        Assert.Equal(3, profile.NeedsAdminCount);
        Assert.True(profile.HasAdminNote);
        Assert.Contains("3", profile.AdminNote, StringComparison.Ordinal);
        Assert.EndsWith(AdminNeeded.RemedyMachineWide, profile.AdminNote, StringComparison.Ordinal);
    }

    [Fact]
    public void The_profile_note_states_the_problem_once()
    {
        // The count sentence already says what is wrong, in better words than the general
        // explanation does. Following it with "this change is machine-wide, and this copy
        // cannot make it" made the card say the same thing twice in a row, which is what it
        // did on screen until somebody read it.
        var note = Profile(machineScope: false, "one").AdminNote;

        Assert.DoesNotContain(Strings.Get("admin.explain.machine"), note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_that_can_be_applied_in_full_says_nothing_about_rights()
    {
        var profile = Profile(machineScope: true, "one", "two");

        Assert.False(profile.HasAdminNote);
        Assert.Empty(profile.AdminNote);
    }

    // --------------------------------------------------------------------------- the wording

    [Fact]
    public void The_advice_differs_by_what_would_actually_fix_it()
    {
        // Two situations, two answers, and mixing them up wastes somebody's time. A cleanup
        // target is emptied by this process, so the answer is to start it with more rights. A
        // machine-wide tweak has a standing way to be applied without an elevated window --
        // the service, one approval, once -- and sending somebody to restart the program
        // instead would be telling them to do the awkward thing forever.
        Assert.Contains("administrator", AdminNeeded.Explain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service", AdminNeeded.ExplainMachineWide, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(AdminNeeded.Explain, AdminNeeded.ExplainMachineWide);
    }

    [Fact]
    public void It_is_said_in_the_reader_s_language()
    {
        Strings.Language = Language.German;

        Assert.Contains("Administrator", AdminNeeded.Badge, StringComparison.Ordinal);
        Assert.NotEqual("admin.explain", AdminNeeded.Explain);
        Assert.Contains("Administrator", AdminNeeded.Explain, StringComparison.Ordinal);
    }
}
