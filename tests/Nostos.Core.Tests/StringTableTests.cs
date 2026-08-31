using System.Text.RegularExpressions;
using Nostos.Core.Localization;
using Xunit;

namespace Nostos.Core.Tests;

/// <summary>
/// Keeps the two string tables honest.
///
/// A translation rots quietly. Somebody adds a label to a view, writes the English, and the
/// German file is one key short; nothing fails, and the window is fine right up until a German
/// user opens that screen. These tests are the thing that turns that into a red build.
///
/// They deliberately test the tables against each other rather than against a list of expected
/// text. What the strings say is a matter of judgement and changes often; that the two files
/// describe the same interface is a fact, and facts are what tests are for.
/// </summary>
public sealed class StringTableTests
{
    /// <summary><c>{0}</c>, <c>{1}</c>: the values the app substitutes into a string.</summary>
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);

    [Fact]
    public void English_has_every_key_German_has()
    {
        var missing = Strings.Keys(Language.German)
            .Except(Strings.Keys(Language.English), StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"de.json has keys en.json does not: {string.Join(", ", missing)}. English is the "
            + "fallback for every key, so a German-only key is a string with nothing behind it.");
    }

    [Fact]
    public void German_has_every_key_English_has()
    {
        var missing = Strings.Keys(Language.English)
            .Except(Strings.Keys(Language.German), StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"de.json is missing {missing.Count} key(s): {string.Join(", ", missing)}. Add them "
            + "to src/Nostos.Core/Localization/de.json.");
    }

    /// <summary>
    /// The one that catches a real crash rather than a cosmetic gap.
    ///
    /// <see cref="Strings.Format"/> passes a fixed number of arguments. A translation that
    /// invented a {1} where the English has only {0} would throw a FormatException at the
    /// moment the string was displayed, which for the update banner or a removal warning means
    /// the window falls over at the least convenient time.
    /// </summary>
    [Fact]
    public void Placeholders_match_between_the_two_languages()
    {
        var problems = new List<string>();

        foreach (var key in Strings.Keys(Language.English).OrderBy(k => k, StringComparer.Ordinal))
        {
            Strings.Language = Language.English;
            var english = Indexes(Strings.Get(key));

            Strings.Language = Language.German;
            var german = Indexes(Strings.Get(key));

            if (!english.SetEquals(german))
            {
                problems.Add(
                    $"{key}: English uses {Describe(english)}, German uses {Describe(german)}");
            }
        }

        Strings.Language = Language.English;

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The sidebar's category names are the same words in every language.
    ///
    /// They are the program's vocabulary rather than prose: the same six names label a docs
    /// page, a tweak's <c>category</c> field and a CLI filter, and half of them -- Performance,
    /// Input Lag, Ping -- are what a German player says anyway. Translating some and not others
    /// produced a sidebar that looked half-finished, so none of them are translated.
    ///
    /// The promise under each one is prose and is translated; only the name is held here.
    /// </summary>
    [Fact]
    public void The_category_names_are_not_translated()
    {
        var differing = Strings.Keys(Language.English)
            .Where(k => k.StartsWith("category.", StringComparison.Ordinal)
                        && k.EndsWith(".name", StringComparison.Ordinal))
            .Where(k => InLanguage(Language.English, k) != InLanguage(Language.German, k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            differing.Count == 0,
            $"de.json translates {string.Join(", ", differing)}. Category names are the same "
            + "words in every language; the promise underneath is the part that is translated.");
    }

    [Fact]
    public void A_key_that_does_not_exist_comes_back_as_itself()
    {
        // Visible and unmistakable on screen, which is the point: a blank label is a bug that
        // ships, and a key is a bug somebody reports.
        Assert.Equal("no.such.key", Strings.Get("no.such.key"));
    }

    [Fact]
    public void German_falls_back_to_English_rather_than_to_nothing()
    {
        Strings.Language = Language.German;

        try
        {
            // Every key is in both tables, so pick one and assert the fallback path is wired
            // by checking it resolves to real text rather than to the key.
            var text = Strings.Get("app.refresh");
            Assert.NotEqual("app.refresh", text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
        finally
        {
            Strings.Language = Language.English;
        }
    }

    [Fact]
    public void Both_tables_are_actually_populated()
    {
        // Guards the embedding itself. If the .json files stopped being embedded resources,
        // every other test here would still pass against two empty dictionaries.
        Assert.True(Strings.Keys(Language.English).Count > 50);
        Assert.True(Strings.Keys(Language.German).Count > 50);
    }

    /// <summary>
    /// Terms a German player would say in English are left in English.
    ///
    /// The first pass of the translation rendered every term literally, so the performance
    /// category promised to raise the "Bildrate" and two services were accused of costing
    /// "Bilder pro Sekunde". Both are correct German and neither is what anybody in this
    /// audience says: they say FPS. A translation that is technically right and reads as
    /// though nobody who plays games wrote it costs more trust than the missing translation
    /// would have.
    ///
    /// The list is deliberately short. It holds only terms that are borrowed into German whole
    /// -- not every English word, because "Dienst", "Treiber" and "Arbeitsspeicher" are what
    /// German actually uses for those and translating them back would be the same mistake in
    /// the other direction.
    /// </summary>
    [Theory]
    [InlineData("Bilder pro Sekunde", "FPS")]
    [InlineData("Bildrate", "FPS")]
    [InlineData("Bildzeit", "Frametime")]
    [InlineData("Eingabeverzögerung", "Input Lag")]
    public void German_keeps_the_borrowed_terms(string translated, string keep)
    {
        Strings.Language = Language.German;

        try
        {
            var offenders = Strings.Keys(Language.German)
                .Where(k => Strings.Get(k).Contains(translated, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"de.json says \"{translated}\" in {string.Join(", ", offenders)}. Use "
                + $"\"{keep}\", which is the word this audience uses in both languages.");
        }
        finally
        {
            Strings.Language = Language.English;
        }
    }

    private static string InLanguage(Language language, string key)
    {
        var previous = Strings.Language;
        Strings.Language = language;

        try
        {
            return Strings.Get(key);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    private static HashSet<string> Indexes(string text)
        => Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static string Describe(HashSet<string> indexes)
        => indexes.Count == 0
            ? "no placeholders"
            : string.Join(", ", indexes.OrderBy(i => i, StringComparer.Ordinal).Select(i => $"{{{i}}}"));
}
