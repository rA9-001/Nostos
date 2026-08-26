using Nostos.Core.Profiles;

namespace Nostos.Core.Tests;

/// <summary>
/// Loading rules for profile files.
///
/// A profile is the one thing in this program a user is likely to hand-edit, so the failure
/// modes that matter are the quiet ones: a file that parses fine and then does nothing.
/// </summary>
public sealed class ProfileLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"nostos-profiles-{Guid.NewGuid():n}");

    public ProfileLoaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void A_profile_round_trips_its_tweaks_and_their_selected_options()
    {
        var path = Write("good.json", """
            {
              "name": "example",
              "description": "Does a thing.",
              "tweaks": [
                { "tweakId": "a.one" },
                { "tweakId": "a.two", "options": { "level": "balanced" } }
              ]
            }
            """);

        var profile = ProfileLoader.Load(path);

        Assert.Equal("example", profile.Name);
        Assert.Equal(2, profile.Tweaks.Count);
        Assert.Equal("balanced", profile.Tweaks[1].EffectiveOptions["level"]);
    }

    [Fact]
    public void A_profile_that_would_apply_nothing_is_rejected()
    {
        // Silently applying nothing is worse than failing: the user clicks Apply, every result
        // says success, and the machine is untouched.
        var path = Write("empty.json", """
            { "name": "empty", "description": "Nothing at all." }
            """);

        var error = Assert.Throws<InvalidDataException>(() => ProfileLoader.Load(path));

        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no tweaks", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_profile_in_the_pre_v2_format_says_how_to_fix_itself()
    {
        // The format before the process watcher was removed. Its lists deserialize into
        // nothing, so without this the file looks valid and quietly does nothing.
        var path = Write("legacy.json", """
            {
              "name": "legacy",
              "persistent": [ { "tweakId": "a.one" } ],
              "session": [ { "tweakId": "a.two" } ],
              "trigger": { "executables": ["game.exe"] }
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() => ProfileLoader.Load(path));

        Assert.Contains("older profile", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tweaks", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_default_to_empty_rather_than_null()
    {
        var path = Write("bare.json", """
            { "name": "bare", "tweaks": [ { "tweakId": "a.one" } ] }
            """);

        var profile = ProfileLoader.Load(path);

        Assert.Empty(profile.Tweaks[0].EffectiveOptions);
        Assert.Equal("", profile.Description);
    }

    [Fact]
    public void The_shipped_profiles_all_load()
    {
        // The three defaults are seeded into every fresh install, so a mistake in one of them
        // is a mistake every new user meets first.
        var repo = FindRepositoryRoot();
        var shipped = Path.Combine(repo, "profiles");

        var profiles = ProfileLoader.LoadDirectory(shipped);

        Assert.NotEmpty(profiles);
        foreach (var profile in profiles)
            Assert.NotEmpty(profile.Tweaks);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
