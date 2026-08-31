using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core.Localization;

namespace Nostos.Tweaks.Declarative;

/// <summary>A tweak's text in one language. Every field is optional.</summary>
public sealed record TweakTranslation
{
    public string? Title { get; init; }
    public string? Summary { get; init; }

    /// <summary>Keyed by choice id.</summary>
    public Dictionary<string, ChoiceTranslation>? Choices { get; init; }
}

/// <summary>A choice's text, and its options'.</summary>
public sealed record ChoiceTranslation
{
    public string? Title { get; init; }
    public string? Description { get; init; }

    /// <summary>Keyed by option id.</summary>
    public Dictionary<string, OptionTranslation>? Options { get; init; }
}

public sealed record OptionTranslation
{
    public string? Title { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// The catalog's text in a language other than English.
///
/// A separate file per language, keyed by tweak id, rather than extra fields on each catalog
/// entry. Three reasons, in order of how much they matter:
///
/// The English catalog stays readable. registry.json is already the file contributors spend the
/// most time in, and doubling every entry to carry a second language would bury the registry
/// paths that are the actual subject of the file.
///
/// A translator can be handed one file. It contains prose and nothing else: no hives, no value
/// kinds, no risk ratings to accidentally edit.
///
/// And a translation is allowed to be incomplete. Every field is optional and every lookup
/// falls back to the English, so a tweak added today shows up in English tonight and in German
/// whenever somebody writes the sentence, without a build ever breaking in between.
/// </summary>
public static class CatalogTranslations
{
    private static readonly Dictionary<Language, IReadOnlyDictionary<string, TweakTranslation>> Loaded = [];

    private static readonly IReadOnlyDictionary<string, TweakTranslation> None =
        new Dictionary<string, TweakTranslation>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The translations for a language, or an empty set for English and for a missing file.</summary>
    public static IReadOnlyDictionary<string, TweakTranslation> For(Language language)
    {
        if (language == Language.English)
            return None;

        if (Loaded.TryGetValue(language, out var table))
            return table;

        table = Load(Strings.CodeOf(language));
        Loaded[language] = table;
        return table;
    }

    /// <summary>The translation for one tweak, or null when there is none.</summary>
    public static TweakTranslation? Find(Language language, string tweakId)
        => For(language).TryGetValue(tweakId, out var translation) ? translation : null;

    private static IReadOnlyDictionary<string, TweakTranslation> Load(string code)
    {
        var assembly = typeof(CatalogTranslations).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".Catalog.{code}.json", StringComparison.OrdinalIgnoreCase));

        if (name is null)
            return None;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return None;

        var parsed = JsonSerializer.Deserialize(
            stream, TranslationJsonContext.Default.DictionaryStringTweakTranslation);

        return parsed is null
            ? None
            : new Dictionary<string, TweakTranslation>(parsed, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The translation file's format. Its own context, because it is its own file format: prose
/// keyed by id, with no enums and nothing required.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, TweakTranslation>))]
public sealed partial class TranslationJsonContext : JsonSerializerContext;
