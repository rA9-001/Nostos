using System.Globalization;
using System.Text.Json;
using Nostos.App.ViewModels;
using Nostos.App.Views;
using Nostos.Core.Abstractions;
using Nostos.Core.Json;
using Nostos.Core.Localization;
using Nostos.Core.Profiles;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// The text the window puts on screen for things that arrive already written in English.
///
/// Three kinds of text reach the window with the English already in it: a profile's
/// description, which is a JSON file on disk that a user can edit; a tweak's "not applicable"
/// reason, which is produced by a service running as SYSTEM that has no user and therefore no
/// language; and a tweak's raw state, which is registry value names. The first two are
/// translated at the point of display, and the third deliberately is not.
///
/// Every one of them has the same shape: a key, and the English as the fallback. These tests
/// pin the fallback down as hard as the translation, because the fallback is the case that
/// happens on somebody else's machine — a profile they wrote, a tweak added after the German
/// file was last touched, a service built before the key existed.
/// </summary>
public sealed class DisplayTextTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static string Describe(ProfileSummary profile)
        => new ProfileViewModel(profile, _ => null).Description;

    [Fact]
    public void A_shipped_profile_is_described_in_the_readers_language()
    {
        var basic = new ProfileSummary("basic", "whatever the file says", 21);

        Strings.Language = Language.English;
        var english = Describe(basic);

        Strings.Language = Language.German;
        var german = Describe(basic);

        Assert.NotEqual(english, german);
        Assert.DoesNotContain("whatever the file says", german, StringComparison.Ordinal);
        Assert.Contains("Neustart", german, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_somebody_wrote_themselves_shows_their_own_words()
    {
        // The case that decides the design. Profiles are files on disk and the set of them is
        // open, so the text in the file has to win whenever nobody has translated that name.
        Strings.Language = Language.German;

        Assert.Equal(
            "my own preset, in my own words",
            Describe(new ProfileSummary("mine", "my own preset, in my own words", 3)));
    }

    [Fact]
    public void A_not_applicable_reason_is_translated_through_the_key_beside_it()
    {
        var status = Reason(
            "notapplicable.processgone", "process 4321 is not running", ["4321"]);

        Strings.Language = Language.German;
        var german = new TweakItemViewModel(status).NotApplicableReason;

        Assert.NotNull(german);
        Assert.DoesNotContain("is not running", german, StringComparison.Ordinal);

        // The argument survives the substitution. A translated sentence that lost the number
        // would be worse than the English one it replaced.
        Assert.Contains("4321", german, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reason_with_no_key_is_shown_as_the_service_sent_it()
    {
        // What a service built before the key existed sends, and what the CLI has always sent.
        Strings.Language = Language.German;

        Assert.Equal(
            "something only this build knows how to say",
            new TweakItemViewModel(
                    Reason(null, "something only this build knows how to say", null))
                .NotApplicableReason);
    }

    [Fact]
    public void A_reason_whose_key_nobody_has_translated_falls_back_to_the_English()
    {
        Strings.Language = Language.German;

        Assert.Equal(
            "a reason from a newer build",
            new TweakItemViewModel(
                    Reason("notapplicable.invented", "a reason from a newer build", []))
                .NotApplicableReason);
    }

    [Fact]
    public void The_raw_state_is_not_translated_in_either_language()
    {
        // Registry value names and the numbers behind them. Somebody comparing this window
        // against regedit or a forum post has to see the same characters in all three.
        var tweak = FakeBackend.Tweak("gpu.hags");

        Strings.Language = Language.English;
        var english = new TweakItemViewModel(tweak).StateDescription;

        Strings.Language = Language.German;
        Assert.Equal(english, new TweakItemViewModel(tweak).StateDescription);
    }

    /// <summary>
    /// The English in en.json still says what the shipped profile file says.
    ///
    /// The table wins over the file, so if somebody rewrites a profile's description and does
    /// not touch en.json, English users keep reading the old sentence and nobody finds out --
    /// the window is not wrong, it is just stale, which is the failure mode no screenshot
    /// catches. This makes the override invisible by keeping the two identical, which is the
    /// only reason overriding the file in English is acceptable at all.
    ///
    /// Reads the profiles out of the assembly rather than off disk: these are the exact bytes
    /// the app installs on first run, and a copy of them in the test project would be a third
    /// thing to keep in step.
    /// </summary>
    [Fact]
    public void The_English_profile_descriptions_match_the_files_that_ship()
    {
        Strings.Language = Language.English;

        var assembly = typeof(Nostos.App.Startup.Bootstrapper).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Nostos.profiles.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var profile = JsonSerializer.Deserialize(
                new StreamReader(stream).ReadToEnd(), ProfileJsonContext.Default.TweakProfile)!;

            Assert.Equal(
                profile.Description,
                Strings.Translate($"profile.{profile.Name}.description", profile.Description));
        }
    }

    /// <summary>
    /// Every value of every enum the window puts a word on has a key for that word.
    ///
    /// Found three gaps the first time it ran, all of the same shape: the tables had
    /// <c>risk.high</c> and <c>scope.session</c>, which name nothing -- the enum members are
    /// Risky and Process. So a Risky tweak's badge read "risky" and a per-process tweak's read
    /// "process" in a German window, and the two keys that were there translated a value that
    /// could never arrive. Nothing failed: the fallback for a missing key is the enum name
    /// lower-cased, which looks exactly like a word somebody chose.
    ///
    /// This is the reason to test an enum against a table rather than a table against itself.
    /// The parity tests hold en.json and de.json to each other, and they were both wrong in
    /// the same way.
    /// </summary>
    [Fact]
    public void Every_badge_the_window_can_draw_has_a_word_in_both_tables()
    {
        var expected = Enum.GetValues<Risk>().Select(r => $"risk.{r}")
            .Concat(Enum.GetValues<Evidence>().Select(e => $"evidence.{e}"))
            .Concat(Enum.GetValues<TweakScope>().Select(s => $"scope.{s}"))
            .Concat(Enum.GetValues<Risk>().Select(r => $"band.risk.{r}.name"))
            .Concat(Enum.GetValues<Risk>().Select(r => $"band.risk.{r}.description"))
            .Select(key => key.ToLowerInvariant())
            .ToList();

        foreach (var language in Enum.GetValues<Language>())
        {
            var keys = Strings.Keys(language);
            var missing = expected.Where(k => !keys.Contains(k)).ToList();

            Assert.True(
                missing.Count == 0,
                $"{language}: no entry for {string.Join(", ", missing)}. The badge would read "
                + "the enum member's name lower-cased, which is indistinguishable from a "
                + "translation somebody wrote.");
        }
    }

    /// <summary>
    /// The other direction: no key names a value that does not exist.
    ///
    /// Without this, the fix for the test above is to add the missing keys and leave the dead
    /// ones sitting there looking authoritative.
    /// </summary>
    [Theory]
    [InlineData("risk.", typeof(Risk))]
    [InlineData("evidence.", typeof(Evidence))]
    [InlineData("scope.", typeof(TweakScope))]
    public void No_badge_key_names_a_value_that_does_not_exist(string prefix, Type enumType)
    {
        var real = Enum.GetNames(enumType).Select(n => prefix + n.ToLowerInvariant()).ToHashSet();

        var orphans = Strings.Keys(Language.English)
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !real.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"en.json translates {string.Join(", ", orphans)}, which {enumType.Name} has no "
            + "member for. Somebody renamed the member and left the key.");
    }

    /// <summary>
    /// The English in en.json still says what <see cref="TweakCategories"/> says.
    ///
    /// Two things print a category's promise and they read it from different places: the window
    /// looks the key up in the string table, and the CLI, the docs headers and the catalog test
    /// take it straight off the category. So the table can drift from the source and the only
    /// symptom is that `nos categories` and the sidebar quietly disagree -- which is exactly
    /// what happened when "framerate" became "FPS" in the table alone.
    ///
    /// The category is the source of truth. The table's English is a copy, and this is what
    /// keeps it one.
    /// </summary>
    [Fact]
    public void The_English_category_text_matches_the_categories_themselves()
    {
        Strings.Language = Language.English;

        foreach (var category in TweakCategories.All)
        {
            Assert.Equal(category.Name, Strings.Translate($"category.{category.Id}.name", category.Name));
            Assert.Equal(
                category.Promise,
                Strings.Translate($"category.{category.Id}.promise", category.Promise));
        }
    }

    /// <summary>
    /// And no key names a category that is not in the closed set.
    ///
    /// Splitting a bucket leaves its keys behind otherwise, still translated, still looking
    /// authoritative, and naming something the program can no longer produce.
    /// </summary>
    [Fact]
    public void No_category_key_names_a_category_that_does_not_exist()
    {
        var real = TweakCategories.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        var orphans = Strings.Keys(Language.English)
            .Where(k => k.StartsWith("category.", StringComparison.Ordinal))
            .Select(k => k.Split('.')[1])
            .Distinct(StringComparer.Ordinal)
            .Where(id => !real.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"en.json still translates {string.Join(", ", orphans)}, which is not a category any "
            + "more. Delete the keys, in both tables.");
    }

    private static TweakStatusSummary Reason(
        string? key, string english, IReadOnlyList<string>? args)
    {
        var applicable = FakeBackend.Tweak("some.tweak", applicable: false);
        return applicable with
        {
            NotApplicableReason = english,
            NotApplicableReasonKey = key,
            NotApplicableReasonArgs = args,
        };
    }
}
