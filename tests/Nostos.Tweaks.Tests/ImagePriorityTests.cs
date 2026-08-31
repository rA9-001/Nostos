using System.Diagnostics;
using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Tweaks.Native;
using Nostos.Win32.Services;
using Xunit;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// A CPU priority that survives the game closing.
///
/// process.game-tuning raises a process that is already running, which means it has to be done
/// again after every launch; in practice that makes a real tweak into a chore nobody performs.
/// This one writes a value the loader reads at process creation instead.
///
/// Nothing here writes to the registry. The tests cover the parts that decide what would be
/// written -- the constant mapping, the image name, the refusals, and the reconciliation revert
/// performs -- which is where a mistake would be permanent and machine-wide.
/// </summary>
public sealed class ImagePriorityTests
{
    private static readonly ImagePriorityTweak Tweak = new();

    private static TweakContext Context(string? exe = null, string? priority = null, string? processName = null)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (exe is not null)
            options["exe"] = exe;
        if (priority is not null)
            options["priority"] = priority;

        return new TweakContext { Options = options, TargetProcessName = processName };
    }

    // ------------------------------------------------------------------ the constants

    [Theory]
    [InlineData(ProcessPriorityClass.Idle, 1)]
    [InlineData(ProcessPriorityClass.Normal, 2)]
    [InlineData(ProcessPriorityClass.High, 3)]
    [InlineData(ProcessPriorityClass.BelowNormal, 5)]
    [InlineData(ProcessPriorityClass.AboveNormal, 6)]
    public void The_loader_has_its_own_numbering_and_this_is_it(ProcessPriorityClass priority, int expected)
    {
        // Not the framework's values: ProcessPriorityClass.High is 128, and writing 128 here
        // would leave a game permanently at a priority Windows does not recognise. There is no
        // way to notice that from the UI, which is why it is pinned in a test.
        Assert.Equal(expected, ImagePriority.ToIfeo(priority));
        Assert.Equal(priority, ImagePriority.FromIfeo(expected));
    }

    [Fact]
    public void Realtime_has_no_permanent_form()
    {
        // Refused one layer lower than the tweak refuses it, so that no future caller can reach
        // it by a different route. A realtime setting that applies at process creation takes
        // effect before anything is running that could take it back.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImagePriority.ToIfeo(ProcessPriorityClass.RealTime));
    }

    [Fact]
    public void An_unrecognised_value_is_described_rather_than_guessed_at()
    {
        Assert.Null(ImagePriority.FromIfeo(4));
        Assert.Equal("unrecognised (4)", ImagePriority.Describe(4));
        Assert.Equal("High", ImagePriority.Describe(3));
    }

    // ------------------------------------------------------------------ the image name

    [Theory]
    [InlineData("cs2", "cs2.exe")]
    [InlineData("cs2.exe", "cs2.exe")]
    [InlineData("CS2.EXE", "CS2.EXE")]
    [InlineData(@"C:\Program Files\Game\cs2.exe", "cs2.exe")]
    [InlineData("  cs2.exe  ", "cs2.exe")]
    [InlineData("\"C:\\Games\\cs2.exe\"", "cs2.exe")]
    public void Three_ways_of_spelling_an_executable_become_one(string given, string expected)
    {
        // The picker hands over a process name with no extension, somebody typing it writes
        // "cs2.exe", and somebody who copied a shortcut target pastes the whole path. The loader
        // matches the bare file name, so all of them have to arrive at the same key.
        Assert.Equal(expected, ImagePriority.NormaliseImageName(given));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Games\")]
    public void A_name_that_cannot_become_a_key_is_refused(string given)
        => Assert.Throws<ArgumentException>(() => ImagePriority.NormaliseImageName(given));

    // ------------------------------------------------------------------ naming the target

    [Fact]
    public async Task Without_an_executable_it_says_so_rather_than_doing_nothing()
    {
        var applicability = await Tweak.CheckApplicabilityAsync(Context());

        Assert.False(applicability.IsApplicable);
        Assert.Equal("notapplicable.noexe", applicability.ReasonKey);
    }

    [Fact]
    public async Task A_running_process_names_the_executable_for_you()
    {
        // What the window's picker supplies. It sends a process, but only the name is used --
        // the pid is meaningless to a setting that outlives every process it affects.
        var applicability = await Tweak.CheckApplicabilityAsync(Context(processName: "cs2"));

        Assert.True(applicability.IsApplicable);
    }

    [Fact]
    public async Task The_command_line_can_name_one_that_is_not_running()
    {
        // The case the picker cannot cover, and the more useful one: setting a game up before
        // playing it rather than during.
        var applicability = await Tweak.CheckApplicabilityAsync(Context(exe: "valorant.exe"));

        Assert.True(applicability.IsApplicable);
    }

    [Fact]
    public async Task An_explicit_executable_wins_over_whatever_is_selected()
    {
        // The window keeps a picker selection while the reader moves around the list. An
        // explicit exe= is an answer to the question; a leftover selection is not.
        var context = Context(exe: "valorant.exe", processName: "cs2");
        var snapshot = await Tweak.CaptureAsync(context);

        Assert.Equal("valorant.exe", snapshot.Data["image"]?.GetValue<string>());
    }

    // ------------------------------------------------------------------ the refusals

    [Fact]
    public async Task Only_the_two_priorities_it_offers_can_be_applied()
    {
        // Idle, Below normal and Normal are all writable here and none are offered. A permanent
        // setting that makes a game slower on every launch is not something the catalog should
        // be able to do by accident.
        foreach (var refused in new[] { "Idle", "BelowNormal", "Normal", "RealTime" })
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => Tweak.ApplyAsync(Context(exe: "cs2.exe", priority: refused)));
        }
    }

    [Fact]
    public void It_recommends_the_gentler_one_of_the_two()
    {
        // The opposite of process.game-tuning, which recommends High. The difference is the
        // permanence: a game that hangs at High is harder to click away from, and this arranges
        // it on every launch rather than the one you asked for.
        var priority = Assert.Single(Tweak.Metadata.Choices);

        Assert.Equal("abovenormal", priority.DefaultOption);
        Assert.Equal("abovenormal", Assert.Single(priority.Options, o => o.Recommended).Id);
        Assert.Equal(["abovenormal", "high"], priority.Options.Select(o => o.Id));
    }

    [Fact]
    public void It_is_machine_scoped_and_still_asks_which_program()
    {
        // The pair that made TakesTargetProcess a field rather than something derived from the
        // scope. This writes HKLM and outlives every process it affects, so it is machine-scoped
        // -- and it still has to be told which executable, or it can do nothing at all.
        Assert.Equal(TweakScope.Machine, Tweak.Metadata.Scope);
        Assert.Equal(TweakLifetime.Persistent, Tweak.Metadata.Lifetime);
        Assert.True(Tweak.Metadata.TakesTargetProcess);
        Assert.True(Tweak.Metadata.RequiresElevation);
    }

    [Fact]
    public void The_session_only_one_is_still_the_safer_of_the_two()
    {
        // Not the same claim, and the catalog should not let them drift into looking alike.
        // game-tuning's worst case is a process exiting; this one survives reboots and applies
        // machine-wide before anything can intervene.
        Assert.Equal(Risk.Safe, new GameProcessTuningTweak().Metadata.Risk);
        Assert.Equal(Risk.Moderate, Tweak.Metadata.Risk);
    }

    [Fact]
    public async Task Asked_about_no_game_in_particular_it_describes_the_machine()
    {
        // What a revert does when it reads the machine back afterwards: it has no exe= to give,
        // because it is undoing all of them. Throwing here made a successful revert print
        // "(read-back failed: No target executable)" underneath it, which reads like a failure
        // and is not one.
        var state = await Tweak.ReadAsync(Context());

        Assert.False(state.IsApplied);
        Assert.DoesNotContain("failed", state.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permanent priority", state.Description, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ capture and revert

    [Fact]
    public async Task Capture_records_every_image_on_the_machine_not_just_the_one_being_set()
    {
        // The whole design of revert rests on this. The journal keeps one snapshot per tweak id
        // and keeps the oldest; this tweak is applied once per game. A snapshot holding only the
        // game being set would leave every later one with no record and no way back.
        var snapshot = await Tweak.CaptureAsync(Context(exe: "cs2.exe"));

        Assert.IsType<JsonObject>(snapshot.Data["images"]);
        Assert.Equal("cs2.exe", snapshot.Data["image"]?.GetValue<string>());
    }

    [Fact]
    public void Revert_removes_games_that_were_set_after_the_capture()
    {
        // Set cs2, then valorant. The snapshot is the one from before cs2, and reverting has to
        // clear both -- otherwise valorant stays at a raised priority permanently, machine-wide,
        // with nothing recording that anyone did it.
        var plan = ImagePriorityTweak.RevertPlan(
            captured: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            current: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["cs2.exe"] = 6,
                ["valorant.exe"] = 3,
            });

        Assert.Equal(2, plan.Count);
        Assert.All(plan, step => Assert.Null(step.Value));
        Assert.Equal(["cs2.exe", "valorant.exe"], plan.Select(s => s.Image).Order());
    }

    [Fact]
    public void Revert_restores_a_value_somebody_else_had_set_rather_than_deleting_it()
    {
        // IFEO is not ours. An image that already had a priority when we arrived had it from an
        // installer, another tool or the user, and revert means "as it was", not "as if empty".
        var plan = ImagePriorityTweak.RevertPlan(
            captured: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["theirs.exe"] = 5 },
            current: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["theirs.exe"] = 3,
                ["ours.exe"] = 6,
            });

        Assert.Contains(plan, s => s.Image == "theirs.exe" && s.Value == 5);
        Assert.Contains(plan, s => s.Image == "ours.exe" && s.Value is null);
    }

    [Fact]
    public void Revert_puts_back_something_that_has_since_been_deleted_entirely()
    {
        var plan = ImagePriorityTweak.RevertPlan(
            captured: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["gone.exe"] = 6 },
            current: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(("gone.exe", (int?)6), Assert.Single(plan));
    }

    [Fact]
    public void Reverting_a_machine_that_is_already_back_does_nothing()
    {
        // Revert has to be idempotent: the engine can run it twice, and `nos revert --all`
        // reaches it after a partial one.
        var same = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["theirs.exe"] = 5 };

        Assert.Empty(ImagePriorityTweak.RevertPlan(same, same));
    }

    [Fact]
    public void Image_names_are_matched_the_way_the_filesystem_matches_them()
    {
        // Windows does not distinguish CS2.EXE from cs2.exe, and a revert that did would leave
        // the key behind.
        var plan = ImagePriorityTweak.RevertPlan(
            captured: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["CS2.EXE"] = 6 },
            current: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["cs2.exe"] = 6 });

        Assert.Empty(plan);
    }

    [Fact]
    public void A_snapshot_written_without_the_map_reverts_nothing_rather_than_everything()
    {
        // Defensive: a snapshot from a build before this field existed, or a truncated one. The
        // safe reading of "no record" is "we do not know what to restore", and clearing every
        // permanent priority on the machine is not a reasonable guess.
        var snapshot = TweakSnapshot.Create("process.persistent-priority", new JsonObject());

        Assert.Null(ImagePriorityTweak.Captured(snapshot));
    }

    [Fact]
    public void An_empty_map_and_no_map_mean_opposite_things()
    {
        // The distinction the null exists for. A machine that genuinely had no permanent
        // priorities captures an empty map, and reverting it correctly removes every one that
        // has been set since. A snapshot with no map at all knows nothing, and clearing the
        // machine on the strength of that would delete entries nothing to do with this program.
        var empty = TweakSnapshot.Create(
            "process.persistent-priority", new JsonObject { ["images"] = new JsonObject() });

        Assert.NotNull(ImagePriorityTweak.Captured(empty));
        Assert.Empty(ImagePriorityTweak.Captured(empty)!);
    }
}
