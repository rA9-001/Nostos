using System.Text.Json;
using Nostos.Core.Abstractions;
using Nostos.Tweaks.Native;

namespace Nostos.Tweaks.Declarative;

/// <summary>
/// Reads one catalog file out of this assembly.
///
/// The files are embedded rather than sitting beside the executable so that a single-file build
/// is still a single file, and so a binary is self-describing: whatever it says the catalog is,
/// is what it will do.
/// </summary>
internal static class EmbeddedCatalog
{
    /// <summary>Every embedded catalog file whose name starts with <paramref name="prefix"/>.</summary>
    public static IEnumerable<string> Read(string prefix)
    {
        var assembly = typeof(EmbeddedCatalog).Assembly;

        var names = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".json", StringComparison.Ordinal))
            .Where(n => FileName(n).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
            throw new InvalidDataException($"No embedded catalog file named '{prefix}*.json'.");

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }

    /// <summary>"Nostos.Tweaks.Catalog.services.json" -> "services.json".</summary>
    private static string FileName(string resourceName)
    {
        var parts = resourceName.Split('.');
        return parts.Length < 2 ? resourceName : $"{parts[^2]}.{parts[^1]}";
    }
}

/// <summary>
/// One Windows service this tool is willing to move off Automatic start.
///
/// Data, not code, for the same reason registry tweaks are: everything that varies between the
/// thirty-five entries is a string, and thirty-five near-identical constructor calls in a C#
/// file is a list wearing a costume. The behaviour they all share -- the Manual/Disabled
/// choice, the refusal to touch a protected service, the capture and revert -- lives once, in
/// <see cref="WindowsServiceTweak"/>.
/// </summary>
public sealed record ServiceTweakDefinition
{
    public required string Id { get; init; }

    /// <summary>SCM service name, e.g. "WSearch". Not the display name.</summary>
    public required string Service { get; init; }

    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Category { get; init; }

    /// <summary>
    /// Required, and deliberately not defaulted.
    ///
    /// The JSON source generator builds this record with an object initializer and passes
    /// `default` for anything the file omits -- so an optional Evidence would come back as
    /// <see cref="Evidence.Measured"/>, which is the flattering answer and the wrong one. An
    /// entry that forgets to say what its evidence is fails to load instead.
    /// </summary>
    public required Evidence Evidence { get; init; }

    /// <summary>Moderate unless an entry says otherwise. Nullable for the reason above.</summary>
    public Risk? Risk { get; init; }

    public IReadOnlyList<string> Tags { get; init => field = value ?? ["service"]; } = ["service"];

    public WindowsServiceTweak ToTweak() => new(
        Id, Service, Title, Summary, Category, Evidence, Risk ?? Core.Abstractions.Risk.Moderate, Tags);
}

/// <summary>
/// One per-adapter NDIS setting that costs latency by design.
///
/// Same reasoning as <see cref="ServiceTweakDefinition"/>: the four entries differ only in
/// which keywords they look for and what to say when a machine has none of them.
/// </summary>
public sealed record AdapterTweakDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }

    /// <summary>
    /// NDIS advanced-property names to look for, in vendor spelling.
    ///
    /// Listing several costs nothing: a keyword a machine does not already have is skipped,
    /// never created.
    /// </summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>The value to write. REG_SZ, even when it reads like a number.</summary>
    public required string Target { get; init; }

    /// <summary>What to tell the user when no adapter on this PC has any of the keywords.</summary>
    public required string AbsentReason { get; init; }

    public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];

    public NetworkAdapterTweak ToTweak() => new(Id, Title, Summary, Keywords, Target, AbsentReason, Tags);
}

public static class ServiceTweakCatalog
{
    public static IReadOnlyList<ServiceTweakDefinition> Parse(string json)
        => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.ListServiceTweakDefinition)
           ?? throw new InvalidDataException("services.json did not contain an array.");

    public static IReadOnlyList<ServiceTweakDefinition> LoadEmbedded()
        => [.. EmbeddedCatalog.Read("services").SelectMany(Parse)];
}

public static class AdapterTweakCatalog
{
    public static IReadOnlyList<AdapterTweakDefinition> Parse(string json)
        => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.ListAdapterTweakDefinition)
           ?? throw new InvalidDataException("adapters.json did not contain an array.");

    public static IReadOnlyList<AdapterTweakDefinition> LoadEmbedded()
        => [.. EmbeddedCatalog.Read("adapters").SelectMany(Parse)];
}
