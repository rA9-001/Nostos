using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;
using Microsoft.Win32;

namespace Nostos.Tweaks.Declarative;

/// <summary>One registry value a tweak wants to set.</summary>
public sealed record RegistryValueSpec
{
    public required string Hive { get; init; }
    public required string Key { get; init; }

    /// <summary>Value name. Empty string targets the key's default value.</summary>
    public string Name { get; init => field = value ?? ""; } = "";

    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;

    /// <summary>Desired value as text. DWORDs accept decimal or "0x"-prefixed hex.</summary>
    public required string Value { get; init; }

    public RegistryValueRef ToRef() => new(Hive, Key, Name);

    public object DecodedValue() => RegistryAccess.Decode(Value, Kind);
}

/// <summary>
/// A user-selectable setting on a declarative tweak.
///
/// Mirrors <see cref="TweakChoice"/> but adds the registry values each option writes, which is
/// the part Core deliberately knows nothing about.
/// </summary>
public sealed record RegistryChoiceDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>Id of the option used when the user has not picked one.</summary>
    public required string Default { get; init; }

    public required IReadOnlyList<RegistryChoiceOptionDefinition> Options
    {
        get;
        init => field = value ?? [];
    }

    public TweakChoice ToChoice() => new()
    {
        Id = Id,
        Title = Title,
        Description = Description,
        DefaultOption = Default,
        Options = [.. Options.Select(o => o.ToOption())],
    };
}

/// <summary>One option of a <see cref="RegistryChoiceDefinition"/>, and what it writes.</summary>
public sealed record RegistryChoiceOptionDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public bool Recommended { get; init; }

    /// <summary>
    /// Values this option writes, on top of the tweak's common values.
    ///
    /// An option may legitimately write nothing -- "leave it alone" is a valid choice, and
    /// modelling it as an empty list is better than making the user untick the tweak.
    /// </summary>
    public IReadOnlyList<RegistryValueSpec> Values { get; init => field = value ?? []; } = [];

    public TweakChoiceOption ToOption() => new()
    {
        Id = Id,
        Title = Title,
        Description = Description,
        Recommended = Recommended,
    };
}

/// <summary>
/// A tweak expressed entirely as data.
///
/// This is the contributor surface: adding a registry tweak is a JSON object and a docs page,
/// with no C# and no build step. Anything that cannot be expressed here — because it needs to
/// call an API, or its revert is not "put the old value back" — becomes a class in Native\
/// instead, and that friction is deliberate.
/// </summary>
public sealed record RegistryTweakDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Category { get; init; }

    public TweakScope Scope { get; init; } = TweakScope.Machine;
    public TweakLifetime Lifetime { get; init; } = TweakLifetime.Persistent;
    public required Risk Risk { get; init; }
    public required Evidence Evidence { get; init; }

    public bool RequiresReboot { get; init; }

    /// <summary>Defaults to true for machine scope, false for per-user keys.</summary>
    public bool? RequiresElevation { get; init; }

    /// <summary>Minimum Windows build this tweak exists on. 0 means "any supported build".</summary>
    public int MinBuild { get; init; }

    /// <summary>Set when a tweak is a straight regression on battery-powered machines.</summary>
    public bool DesktopOnly { get; init; }

    public required IReadOnlyList<RegistryValueSpec> Values { get; init; }

    public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> ConflictsWith { get; init => field = value ?? []; } = [];

    /// <summary>Settings the user picks between. Usually empty. See <see cref="TweakChoice"/>.</summary>
    public IReadOnlyList<RegistryChoiceDefinition> Choices { get; init => field = value ?? []; } = [];

    /// <summary>
    /// The registry values to write for a given set of selections: the tweak's common values
    /// plus whatever each selected option contributes.
    /// </summary>
    public IReadOnlyList<RegistryValueSpec> ValuesFor(IReadOnlyDictionary<string, string> selections)
    {
        if (Choices.Count == 0)
            return Values;

        var values = new List<RegistryValueSpec>(Values);
        foreach (var choice in Choices)
        {
            selections.TryGetValue(choice.Id, out var selected);
            var chosen = choice.ToChoice().Resolve(selected);

            var option = choice.Options.First(o =>
                string.Equals(o.Id, chosen.Id, StringComparison.OrdinalIgnoreCase));

            values.AddRange(option.Values);
        }

        return values;
    }

    /// <summary>
    /// Every registry value this tweak could write under any selection, deduplicated.
    ///
    /// This is what gets captured before an apply. Snapshotting only the selected option's
    /// values would lose the ability to undo a previous selection's writes.
    /// </summary>
    public IReadOnlyList<RegistryValueSpec> AllReachableValues
        => field ??= [.. Values
            .Concat(Choices.SelectMany(c => c.Options).SelectMany(o => o.Values))
            .DistinctBy(v => $"{v.Hive}|{v.Key}|{v.Name}".ToLowerInvariant())];

    /// <summary>Human-readable summary of the current selections, for logs and the journal.</summary>
    public string DescribeSelections(IReadOnlyDictionary<string, string> selections)
        => string.Join(", ", Choices.Select(c =>
        {
            selections.TryGetValue(c.Id, out var selected);
            return $"{c.Title}: {c.ToChoice().Resolve(selected).Title}";
        }));

    public bool EffectiveRequiresElevation => RequiresElevation ?? Scope == TweakScope.Machine;

    public TweakMetadata ToMetadata() => new()
    {
        Id = Id,
        Title = Title,
        Summary = Summary,
        Category = Category,
        Scope = Scope,
        Lifetime = Lifetime,
        Risk = Risk,
        Evidence = Evidence,
        RequiresReboot = RequiresReboot,
        RequiresElevation = EffectiveRequiresElevation,
        Tags = Tags,
        ConflictsWith = ConflictsWith,
        Choices = [.. Choices.Select(c => c.ToChoice())],
    };
}

public static class RegistryTweakCatalog
{
    public static IReadOnlyList<RegistryTweakDefinition> Parse(string json)
        => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.ListRegistryTweakDefinition)
           ?? throw new InvalidDataException("Catalog file did not contain a tweak array.");

    /// <summary>Loads every embedded catalog file shipped with this assembly.</summary>
    public static IReadOnlyList<RegistryTweakDefinition> LoadEmbedded()
    {
        var assembly = typeof(RegistryTweakCatalog).Assembly;
        var definitions = new List<RegistryTweakDefinition>();

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;
            using var reader = new StreamReader(stream);
            definitions.AddRange(Parse(reader.ReadToEnd()));
        }

        return definitions;
    }
}
