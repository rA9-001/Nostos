using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core.Engine;
using Nostos.Core.Json;

namespace Nostos.Core.Profiles;

/// <summary>
/// A named set of tweaks the user can apply in one go.
///
/// Profiles are plain JSON so a user can diff, share and audit them without running anything.
///
/// A profile is a shortcut, not a mode. Applying one is exactly equivalent to applying each
/// tweak in it by hand, and nothing watches for a reason to apply or undo it later. That is a
/// deliberate limit: see docs/architecture.md on why this program does not run while you play.
/// </summary>
public sealed record TweakProfile
{
    public required string Name { get; init; }

    public string Description { get; init => field = value ?? ""; } = "";

    /// <summary>The tweaks this profile applies, with any per-tweak choices already made.</summary>
    public IReadOnlyList<TweakSelection> Tweaks { get; init => field = value ?? []; } = [];
}

public static class ProfileLoader
{
    public static TweakProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize(json, ProfileJsonContext.Default.TweakProfile)
            ?? throw new InvalidDataException($"'{path}' did not contain a profile.");

        // A profile that parses but applies nothing is the worst outcome available: the user
        // clicks Apply, everything reports success, and the machine is untouched. Refuse it.
        //
        // The specific way to land here is an older file using the "persistent" and "session"
        // lists from when profiles could activate themselves per-game, so say so rather than
        // leaving someone comparing their file against a schema.
        if (profile.Tweaks.Count == 0)
        {
            var hint = json.Contains("\"persistent\"", StringComparison.OrdinalIgnoreCase)
                       || json.Contains("\"session\"", StringComparison.OrdinalIgnoreCase)
                ? " It looks like an older profile: merge its \"persistent\" and \"session\" lists " +
                  "into a single \"tweaks\" list."
                : "";

            throw new InvalidDataException(
                $"Profile '{profile.Name}' in '{path}' lists no tweaks, so applying it would do " +
                $"nothing.{hint}");
        }

        return profile;
    }

    public static IReadOnlyList<TweakProfile> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        var profiles = new List<TweakProfile>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            profiles.Add(Load(file));
        return profiles;
    }

    public static void Save(TweakProfile profile, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(profile, ProfileJsonContext.Default.TweakProfile));
}
