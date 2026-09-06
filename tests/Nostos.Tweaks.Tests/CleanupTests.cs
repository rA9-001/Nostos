using Nostos.Core.Localization;
using Nostos.Win32.Cleanup;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Emptying caches, and the rules that keep it from emptying anything else.
///
/// The whole safety argument is that the list is a fixed set of named places rather than a search
/// for folders that look disposable. That is not a theoretical distinction: on the machine this
/// was written on, <c>Steam\steamapps\downloading</c> held 172 GB of a paused game update, in a
/// folder whose name and contents look exactly like staging nobody wants.
///
/// These tests hold the list, the containment rules and the age rule. Everything else here is
/// file deletion, which is not interesting until it escapes the folder it was pointed at.
/// </summary>
public sealed class CleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nostos-clean-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Make(string relative, string content = "x", DateTime? written = null)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        if (written is { } when)
            File.SetLastWriteTimeUtc(path, when);

        return path;
    }

    private CleanupTarget Target(TimeSpan? age = null, string pattern = "*")
        => new("test.target", CleanupGroup.Temp, _root, pattern, age);

    // ------------------------------------------------------------------ the list itself

    [Fact]
    public void Everything_the_catalogue_refuses_is_written_down_with_a_reason()
    {
        // "We do not touch that" is only a promise if somebody can read it, and a future change
        // that adds one of these has to argue with the reason rather than simply not think of it.
        Assert.NotEmpty(CleanupTargets.NeverTouched);
        Assert.All(CleanupTargets.NeverTouched, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n.Path));
            Assert.True(n.Why.Length > 30, $"{n.Path} has no real reason attached");
        });
    }

    [Theory]
    [InlineData("downloading")]
    [InlineData("workshop")]
    [InlineData("Downloads")]
    [InlineData("Prefetch")]
    [InlineData("Windows.old")]
    public void The_places_that_are_refused_are_not_quietly_in_the_list(string fragment)
    {
        // The one that matters is the first: a paused Steam download lives in a folder called
        // "downloading", full of files nothing is reading, next to a launcher's own caches. It
        // is the most expensive thing on the machine to get wrong and the easiest to mistake.
        Assert.DoesNotContain(
            CleanupTargets.All,
            t => t.Path.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            CleanupTargets.NeverTouched,
            n => n.Path.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_target_has_an_id_and_a_path_and_no_two_share_an_id()
    {
        Assert.All(CleanupTargets.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Id)));
        Assert.All(CleanupTargets.All, t => Assert.True(Path.IsPathRooted(t.Path), t.Id));

        Assert.Equal(
            CleanupTargets.All.Count,
            CleanupTargets.All.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Only_scratch_directories_have_an_age_rule()
    {
        // A shader cache is held open or it is not; waiting a day to clear one would only mean
        // the person who came here to fix stutter cannot. Temp is the opposite: what is in it
        // right now is probably an installer's working directory.
        Assert.All(
            CleanupTargets.All.Where(t => t.MinimumAge is not null),
            t => Assert.Equal(CleanupGroup.Temp, t.Group));

        Assert.All(
            CleanupTargets.All.Where(t => t.Group == CleanupGroup.Temp),
            t => Assert.NotNull(t.MinimumAge));
    }

    [Fact]
    public void Every_target_says_what_it_is_and_what_it_costs()
    {
        // A row with no text falls back to its id, so a missing string shows somebody
        // "shaders.nvidia-dx" and a Clean button. The cost line is the one that must never be
        // missing: a size and a button with nothing said about the trade is asking a person to
        // gamble with their own machine.
        var missing = new List<string>();

        foreach (var target in CleanupTargets.All)
        {
            if (Strings.GetOr($"clean.name.{target.Id}", "") is "")
                missing.Add($"clean.name.{target.Id}");

            if (Strings.GetOr($"clean.cost.{target.Id}", "") is "")
                missing.Add($"clean.cost.{target.Id}");
        }

        foreach (var group in Enum.GetValues<CleanupGroup>())
        {
            var key = $"clean.group.{group.ToString().ToLowerInvariant()}";
            if (Strings.GetOr(key, "") is "")
                missing.Add(key);
        }

        Assert.True(missing.Count == 0, "no text for: " + string.Join(", ", missing));
    }

    // ------------------------------------------------------------------ containment

    [Theory]
    [InlineData(@"C:\cache", @"C:\cache\a\b.txt", true)]
    [InlineData(@"C:\cache", @"C:\cache", true)]
    [InlineData(@"C:\cache", @"C:\cache\..\other\b.txt", false)]
    [InlineData(@"C:\cache", @"C:\cacheother\b.txt", false)]
    [InlineData(@"C:\cache", @"C:\Windows\System32", false)]
    public void Nothing_outside_the_target_counts_as_inside_it(string root, string path, bool inside)
    {
        // The backstop for the link check rather than a duplicate of it. Path resolution is
        // where this kind of code goes wrong, and a delete that escapes its root is the one
        // failure this feature must not have. Note the fourth case: a prefix match is not a
        // containment check, and "C:\cacheother" is not in "C:\cache".
        Assert.Equal(inside, Cleanup.Inside(root, path));
    }

    [Fact]
    public void A_junction_out_of_the_folder_is_not_followed()
    {
        // An installer that left a junction under Temp pointing at somebody's documents would
        // otherwise turn "empty a cache" into "delete my documents". Recursive enumeration in
        // the framework follows these, which is why the walk here is written by hand.
        var outside = Path.Combine(_root, "outside");
        var inside = Path.Combine(_root, "cache");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(inside);

        var treasure = Path.Combine(outside, "important.txt");
        File.WriteAllText(treasure, "do not delete me");

        var link = Path.Combine(inside, "link");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // Creating links needs a privilege this machine may not grant. Not a failure.
        }

        Assert.True(Cleanup.IsLink(link));

        var result = Cleanup.Clean(new CleanupTarget("t", CleanupGroup.Temp, inside), elevated: true);

        Assert.True(File.Exists(treasure), "the sweep followed a link out of its own folder");
        Assert.Empty(result.Problems);
    }

    // ------------------------------------------------------------------ emptying

    [Fact]
    public void The_folder_itself_survives_being_emptied()
    {
        // Several programs create their cache directory once at install time and assume it from
        // then on. A tidier machine that loses one of those is not tidier.
        Make("a.txt");
        Make(Path.Combine("deep", "b.txt"));

        var result = Cleanup.Clean(Target(), elevated: true);

        Assert.True(Directory.Exists(_root));
        Assert.Equal(2, result.FilesRemoved);
        Assert.False(Directory.Exists(Path.Combine(_root, "deep")), "empty subfolders should go");
    }

    [Fact]
    public void A_file_younger_than_the_age_rule_is_left_alone()
    {
        var old = Make("old.txt", written: DateTime.UtcNow.AddDays(-3));
        var fresh = Make("fresh.txt");

        var result = Cleanup.Clean(Target(age: TimeSpan.FromHours(24)), elevated: true);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh), "a file written minutes ago is probably in use");
        Assert.Equal(1, result.FilesRemoved);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Only_files_matching_the_pattern_are_touched()
    {
        // The Explorer folder holds the thumbnail databases beside state that is not a cache at
        // all, and emptying the whole thing is how a cleaner loses somebody's taskbar layout.
        var cache = Make("thumbcache_100.db");
        var other = Make("settings.dat");

        Cleanup.Clean(Target(pattern: "*cache_*.db"), elevated: true);

        Assert.False(File.Exists(cache));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void A_machine_wide_target_is_refused_rather_than_half_done_without_elevation()
    {
        var target = new CleanupTarget("t", CleanupGroup.Windows, _root, NeedsElevation: true);
        var kept = Make("a.txt");

        var result = Cleanup.Clean(target, elevated: false);

        Assert.True(File.Exists(kept));
        Assert.Equal(0, result.FilesRemoved);

        // Reported as a fact rather than as a sentence, and not as a problem: nothing went
        // wrong, and nothing was attempted. The window writes the wording, because it is the
        // only part of this program that has a reader and therefore a language -- the same code
        // runs inside a service that has neither.
        Assert.True(result.NeedsAdmin);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_target_that_is_not_on_this_machine_is_not_a_failure()
    {
        var missing = new CleanupTarget("t", CleanupGroup.Temp, Path.Combine(_root, "nope"));

        var finding = Cleanup.Measure(missing, elevated: true);
        var result = Cleanup.Clean(missing, elevated: true);

        Assert.Equal(0, finding.Files);
        Assert.False(finding.HasAnything);
        Assert.Empty(result.Problems);
    }

    // ------------------------------------------------------------------ measuring

    [Fact]
    public void Measuring_changes_nothing()
    {
        var file = Make("a.txt", new string('x', 1000));

        var finding = Cleanup.Measure(Target(), elevated: true);

        Assert.True(File.Exists(file));
        Assert.Equal(1, finding.Files);
        Assert.Equal(1000, finding.Bytes);
    }

    [Fact]
    public void What_the_age_rule_held_back_is_counted_rather_than_hidden()
    {
        // A size that looks far too small next to what Explorer reports needs its explanation
        // standing next to it, not in a support thread.
        Make("old.txt", written: DateTime.UtcNow.AddDays(-3));
        Make("fresh1.txt");
        Make("fresh2.txt");

        var finding = Cleanup.Measure(Target(age: TimeSpan.FromHours(24)), elevated: true);

        Assert.Equal(1, finding.Files);
        Assert.Equal(2, finding.Held);
    }

    [Fact]
    public void A_target_needing_elevation_is_still_measured_when_not_elevated()
    {
        // "You would get 2 GB back if you ran this elevated" is worth knowing, and a row that
        // says only "blocked" invites somebody to elevate for nothing.
        Make("a.txt", new string('x', 500));

        var finding = Cleanup.Measure(
            new CleanupTarget("t", CleanupGroup.Windows, _root, NeedsElevation: true), elevated: false);

        Assert.Equal(500, finding.Bytes);
        Assert.True(finding.NeedsAdmin);
        Assert.False(finding.CanClean);
    }
}
