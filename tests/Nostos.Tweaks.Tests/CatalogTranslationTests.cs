using Nostos.Core.Localization;
using Nostos.Tweaks;
using Nostos.Tweaks.Declarative;
using Xunit;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Keeps the German catalog file pointed at tweaks that exist.
///
/// The translation file is keyed by tweak id, which means it can rot in a way nothing else
/// notices: rename a tweak, and its German title stops being used without any error anywhere.
/// The app would keep working and quietly show that one row in English forever.
///
/// Note what is deliberately NOT asserted: that every tweak has a translation. Coverage is
/// allowed to be partial, so that adding a tweak never requires writing German in the same
/// commit. The test that matters is the other direction.
/// </summary>
public sealed class CatalogTranslationTests
{
    private static readonly IReadOnlyList<string> CatalogIds =
        CatalogFactory.CreateAll().Select(t => t.Metadata.Id).ToList();

    [Fact]
    public void Every_translated_id_is_a_tweak_that_exists()
    {
        var orphans = CatalogTranslations.For(Language.German).Keys
            .Except(CatalogIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"de.json translates {orphans.Count} id(s) that are not in the catalog: "
            + $"{string.Join(", ", orphans)}. They were probably renamed.");
    }

    [Fact]
    public void Translated_choices_and_options_point_at_ones_that_exist()
    {
        var byId = CatalogFactory.CreateAll()
            .ToDictionary(t => t.Metadata.Id, t => t.Metadata, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        foreach (var (id, translation) in CatalogTranslations.For(Language.German))
        {
            if (translation.Choices is not { } choices)
                continue;

            if (!byId.TryGetValue(id, out var tweak))
                continue;

            foreach (var (choiceId, choice) in choices)
            {
                var real = tweak.Choices.FirstOrDefault(c =>
                    string.Equals(c.Id, choiceId, StringComparison.OrdinalIgnoreCase));

                if (real is null)
                {
                    problems.Add($"{id}: no choice '{choiceId}'");
                    continue;
                }

                foreach (var optionId in (IEnumerable<string>?)choice.Options?.Keys ?? [])
                {
                    if (!real.Options.Any(o =>
                            string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        problems.Add($"{id}/{choiceId}: no option '{optionId}'");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The same glossary the string table is held to, applied to the tweak text.
    ///
    /// Two service summaries said a background task "kostet Bilder pro Sekunde" where the
    /// English says it costs frames. Correct German, and not what a German player calls it --
    /// they say FPS. See StringTableTests.German_keeps_the_borrowed_terms for why the list is
    /// this short.
    /// </summary>
    [Theory]
    [InlineData("Bilder pro Sekunde", "FPS")]
    [InlineData("Bildrate", "FPS")]
    [InlineData("Bildzeit", "Frametime")]
    public void German_keeps_the_borrowed_terms(string translated, string keep)
    {
        var offenders = new List<string>();

        foreach (var (id, translation) in CatalogTranslations.For(Language.German))
        {
            void Check(string? text, string where)
            {
                if (text is not null
                    && text.Contains(translated, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(where);
                }
            }

            Check(translation.Title, $"{id}.title");
            Check(translation.Summary, $"{id}.summary");

            foreach (var (choiceId, choice) in translation.Choices ?? [])
            {
                Check(choice.Title, $"{id}/{choiceId}.title");
                Check(choice.Description, $"{id}/{choiceId}.description");

                foreach (var (optionId, option) in choice.Options ?? [])
                {
                    Check(option.Title, $"{id}/{choiceId}/{optionId}.title");
                    Check(option.Description, $"{id}/{choiceId}/{optionId}.description");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"de.json says \"{translated}\" in {string.Join(", ", offenders.Order())}. Use "
            + $"\"{keep}\", which is the word this audience uses in both languages.");
    }

    /// <summary>
    /// The network adapter tweaks' "not applicable" reasons build their translation key from
    /// the tweak id, so a rename would lose the German silently.
    ///
    /// Every other reason in the program names its key as a literal, where a typo is visible
    /// next to the sentence it belongs to. These four cannot: each one names the setting it
    /// went looking for, so there is nothing to share, and deriving the key from the id is the
    /// only way to avoid four more literals that have to agree with four ids by hand. This is
    /// the check that makes deriving it safe.
    ///
    /// Only English is asserted. de.json is held to English by StringTableTests, so a key in
    /// one and not the other is already a failing build.
    /// </summary>
    [Fact]
    public void Every_adapter_tweak_has_a_key_for_its_absent_reason()
    {
        var keys = Strings.Keys(Language.English);

        var missing = AdapterTweakCatalog.LoadEmbedded()
            .Select(a => $"notapplicable.{a.Id}")
            .Where(key => !keys.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"en.json is missing {string.Join(", ", missing)}. NetworkAdapterTweak builds that "
            + "key from the tweak id, so an adapter tweak without one shows English in a "
            + "German window.");
    }

    [Fact]
    public void The_German_file_is_actually_loaded()
    {
        // Guards the embedding. Without this, every other test here passes against an empty
        // dictionary and the app silently shows English.
        Assert.NotEmpty(CatalogTranslations.For(Language.German));
    }

    [Fact]
    public void English_asks_for_no_translations_at_all()
    {
        // English is the language the catalog is written in, so there is nothing to look up
        // and no file to read.
        Assert.Empty(CatalogTranslations.For(Language.English));
    }
}
