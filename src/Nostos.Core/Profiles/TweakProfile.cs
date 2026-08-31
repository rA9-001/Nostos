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

    /// <summary>
    /// Where this profile sits in the list. Lower first; ties fall back to the name.
    ///
    /// The three shipped profiles are a ladder -- Basic, then Intermediate, then Expert -- and
    /// a ladder read in file-name order is Basic, Expert, Intermediate, which says the opposite
    /// of what the names mean. Absent on a profile somebody wrote themselves, which leaves it
    /// at 0 and sorted by name among the others.
    /// </summary>
    public int Order { get; init; }

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

        // Sorted here rather than by each caller, so the window, the CLI and anything else
        // reading this folder agree on the order. Directory enumeration order is the file
        // system's business and is not the order anybody wants to read these in.
        return [.. profiles
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public static void Save(TweakProfile profile, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(profile, ProfileJsonContext.Default.TweakProfile));
}
