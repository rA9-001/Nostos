using Nostos.Win32.Services;
using Xunit;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Reading the startup list, and deciding what a startup entry's approval record means.
///
/// Nothing here writes to the registry. What it covers is the two pieces of decoding that stand
/// between a correct list and a plausible-looking wrong one -- the approval byte and the command
/// line -- because both fail silently. An entry reported backwards looks like a working list.
/// </summary>
public sealed class StartupItemsTests
{
    // ------------------------------------------------------------ the approval byte

    [Theory]
    [InlineData(0x02, true)]   // enabled, never touched
    [InlineData(0x06, true)]   // enabled after being switched back on
    [InlineData(0x03, false)]  // disabled
    [InlineData(0x07, false)]  // disabled, with the upper bits something else set
    public void Bit_zero_is_the_disabled_flag_and_the_rest_of_the_byte_is_not_an_enum(
        byte first, bool expected)
    {
        // The bug this pins. Reading the byte as a value -- 0x02 means on, 0x03 means off --
        // works on most machines and then reports one entry backwards on a machine that has an
        // 0x06 or an 0x07 in it. The machine this was written on has an 0x07: Windows Security's
        // tray icon, switched off, and a value-based reading would have shown it as running.
        byte[] approval = [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.Equal(expected, StartupItems.IsApprovedEnabled(approval));
    }

    [Fact]
    public void No_approval_record_at_all_means_enabled()
    {
        // The common case, and the one that would be wrong in the safe-looking direction: the
        // approval key only gains an entry once something has switched it off, so most startup
        // entries on most machines have no record. Defaulting to disabled would have shown a
        // fresh install as running nothing.
        Assert.True(StartupItems.IsApprovedEnabled(null));
        Assert.True(StartupItems.IsApprovedEnabled([]));
    }

    // ------------------------------------------------------------ command lines

    [Theory]
    [InlineData(@"""C:\Program Files\Steam\steam.exe"" -silent", @"C:\Program Files\Steam\steam.exe")]
    [InlineData(@"""C:\Games\a.exe""", @"C:\Games\a.exe")]
    [InlineData(@"C:\Windows\system32\thing.exe", @"C:\Windows\system32\thing.exe")]
    [InlineData(@"C:\Windows\system32\thing.exe /background", null)]
    public void A_run_command_is_reduced_to_the_program_it_launches(string command, string? expected)
    {
        // The last case is the honest one: unquoted, with spaces, and nothing on this machine at
        // that path. `C:\Program Files\A B\c.exe /x` and `C:\Program.exe Files\A B\c.exe` are
        // the same string, so with no file to check against there is no answer, and null gets a
        // row with no icon rather than an icon taken from the wrong file.
        var resolved = StartupItems.ResolveExecutable(command);

        if (expected is null)
            Assert.Null(resolved);
        else
            Assert.Equal(expected, resolved);
    }

    [Fact]
    public void A_quoted_path_is_taken_at_its_word_even_when_the_file_cannot_be_opened()
    {
        // Quotes remove the ambiguity, so there is nothing left to verify -- and verifying anyway
        // was a real bug. A Store app's launcher under WindowsApps is a zero-length reparse point
        // that File.Exists reports as absent, which silently dropped the path and the icon for
        // every Store-installed startup entry. Teams is exactly that, and is exactly the kind of
        // thing people open this list to find.
        const string command =
            @"""C:\Users\Someone\AppData\Local\Microsoft\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe"" msteams:system-initiated";

        Assert.Equal(
            @"C:\Users\Someone\AppData\Local\Microsoft\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe",
            StartupItems.ResolveExecutable(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_command_resolves_to_nothing(string command)
        => Assert.Null(StartupItems.ResolveExecutable(command));

    // ------------------------------------------------------------ scope

    [Theory]
    [InlineData(StartupSource.MachineRun, true)]
    [InlineData(StartupSource.MachineRun32, true)]
    [InlineData(StartupSource.MachineStartupFolder, true)]
    [InlineData(StartupSource.UserRun, false)]
    [InlineData(StartupSource.UserStartupFolder, false)]
    public void Scope_decides_which_half_of_the_program_can_switch_an_entry(
        StartupSource source, bool machineWide)
    {
        // Not cosmetic. A machine-wide entry lives in HKLM and needs the elevated service; a
        // per-user one has to be written by the app, because HKCU inside a LocalSystem service
        // is SYSTEM's own hive and the write would succeed while changing nothing the signed-in
        // user would ever see.
        var item = new StartupItem("id", "Name", source, "cmd", null, true, "wherever");

        Assert.Equal(machineWide, item.IsMachineWide);
    }

    // ------------------------------------------------------------ the live machine

    [Fact]
    public void The_real_list_reads_without_throwing_and_gives_every_entry_an_id()
    {
        // Against the machine the tests run on, which may legitimately have nothing at startup.
        // What is asserted is the shape: ids unique and non-empty, because the id is what the
        // window and the pipe pass around and a duplicate would switch the wrong program.
        var items = StartupItems.List();

        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Id)));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Location)));
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void An_id_that_names_nothing_is_simply_not_found()
    {
        // The whole security story for the service's startup-set: it resolves the id against the
        // live machine rather than trusting it, so an unprivileged caller cannot reach any part
        // of the registry that is not already a startup entry.
        Assert.Null(StartupWire.Find("user-run:definitely-not-a-real-startup-entry"));
        Assert.Null(StartupWire.Find(@"machine-run:..\..\SomewhereElse"));
    }

    [Fact]
    public void Every_source_says_where_it_lives_in_words_a_person_can_look_up()
    {
        foreach (var source in Enum.GetValues<StartupSource>())
            Assert.False(string.IsNullOrWhiteSpace(StartupItems.LocationOf(source)));
    }
}
