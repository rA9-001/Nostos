using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;
using Nostos.Core.Localization;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// A profile card, and the list of what it would do.
///
/// "Apply 42 tweaks" is a lot to agree to on the strength of one sentence, and the honest answer
/// to "what does this actually change?" used to be to go and read a JSON file. The card opens.
///
/// The rows are resolved against the catalog the window already holds rather than sent with the
/// profile, because a tweak's title, category and risk live in the catalog and are translated.
/// The profile carries ids, and an id is not language.
/// </summary>
public sealed class ProfileViewModelTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static TweakItemViewModel Row(
        string id,
        Risk risk = Risk.Safe,
        string category = TweakCategories.Ping,
        bool applied = false)
        => new(FakeBackend.Tweak(
            id, category, risk: risk, applied: applied, title: $"Title of {id}"));

    private static ProfileViewModel Profile(params string[] ids)
    {
        var catalog = ids.ToDictionary(id => id, id => Row(id), StringComparer.OrdinalIgnoreCase);

        return new ProfileViewModel(
            new ProfileSummary("basic", "A profile.", ids.Length, ids),
            id => catalog.GetValueOrDefault(id));
    }

    [Fact]
    public void A_card_starts_closed()
    {
        var profile = Profile("one", "two");

        Assert.False(profile.IsExpanded);
        Assert.True(profile.CanExpand);
    }

    [Fact]
    public void Opening_it_lists_what_it_would_apply_in_the_profiles_own_order()
    {
        // Order matters and is the profile's, not the catalog's: reading down the list should
        // match reading down the file somebody may have opened beside it.
        var profile = Profile("zeta", "alpha", "mid");
        profile.Toggle();

        Assert.True(profile.IsExpanded);
        Assert.Equal(
            ["Title of zeta", "Title of alpha", "Title of mid"],
            profile.Tweaks.Select(t => t.Title));
    }

    [Fact]
    public void Each_row_carries_the_risk_the_catalog_gives_it()
    {
        var catalog = new Dictionary<string, TweakItemViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["safe.one"] = Row("safe.one"),
            ["risky.one"] = Row("risky.one", Risk.Risky),
        };

        var profile = new ProfileViewModel(
            new ProfileSummary("p", "d", 2, ["safe.one", "risky.one"]),
            id => catalog.GetValueOrDefault(id));

        Assert.Equal(["safe", "risky"], profile.Tweaks.Select(t => t.RiskText));
        Assert.Equal(["RiskSafe", "RiskHigh"], profile.Tweaks.Select(t => t.RiskBrushKey));
    }

    [Fact]
    public void A_tweak_this_build_does_not_have_is_shown_rather_than_dropped()
    {
        // The count on the card comes from the profile. Silently skipping a row it names would
        // leave the list one shorter than the number above it with nothing to explain why.
        var profile = new ProfileViewModel(
            new ProfileSummary("p", "d", 1, ["gone.away"]),
            _ => null);

        var row = Assert.Single(profile.Tweaks);

        Assert.True(row.IsMissing);
        Assert.Equal("gone.away", row.Title);
    }

    [Fact]
    public void The_rows_are_read_in_the_readers_language()
    {
        // RefreshText between the two, because the rows are stable objects now rather than
        // records rebuilt on every read. That is what lets a row carry live state while the
        // profile is being applied, and the price is that the card has to be told when the
        // language changes -- which the window does, in OnLanguageChanged.
        var profile = Profile("one");

        Strings.Language = Language.English;
        profile.RefreshText();
        var english = profile.Tweaks.Single().RiskText;

        Strings.Language = Language.German;
        profile.RefreshText();
        var german = profile.Tweaks.Single().RiskText;

        Assert.Equal("safe", english);
        Assert.Equal("sicher", german);
    }

    [Fact]
    public void A_profile_that_arrived_without_its_list_does_not_offer_to_open()
    {
        // What a service built before the list existed sends. The card then behaves exactly as
        // it did before: a name, a sentence, a count and an Apply button. Better than an arrow
        // that opens onto nothing.
        var profile = new ProfileViewModel(new ProfileSummary("old", "d", 9), _ => null);

        Assert.False(profile.CanExpand);
        Assert.Empty(profile.Tweaks);
    }


    // --------------------------------------------------------------- applied state

    [Fact]
    public void A_row_says_whether_the_machine_already_matches_it()
    {
        // The question somebody opens a profile card to answer is "what would this do to my
        // machine", and on a machine where most of the profile is already applied the honest
        // answer is the rest of it. Without this the card sold every row equally.
        var catalog = new Dictionary<string, TweakItemViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["done"] = Row("done", applied: true),
            ["todo"] = Row("todo", applied: false),
        };

        var profile = new ProfileViewModel(
            new ProfileSummary("p", "d", 2, ["done", "todo"]),
            id => catalog.GetValueOrDefault(id));

        Assert.Equal([true, false], profile.Tweaks.Select(t => t.IsApplied));
        Assert.Equal(["✓", "○"], profile.Tweaks.Select(t => t.StateGlyph));

        // Dimmed once it would change nothing, so the eye lands on the remaining work.
        Assert.True(profile.Tweaks.First().RowOpacity < profile.Tweaks.Last().RowOpacity);
    }

    [Fact]
    public void The_card_header_says_how_much_of_it_is_already_done()
    {
        var catalog = new Dictionary<string, TweakItemViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = Row("a", applied: true),
            ["b"] = Row("b", applied: true),
            ["c"] = Row("c", applied: false),
        };

        var profile = new ProfileViewModel(
            new ProfileSummary("p", "d", 3, ["a", "b", "c"]),
            id => catalog.GetValueOrDefault(id));

        Assert.Equal("2 of 3 already applied", profile.AppliedText);
        Assert.True(profile.HasAppliedText);
    }

    [Fact]
    public void A_profile_with_no_list_does_not_claim_anything_about_what_is_applied()
    {
        // What a service built before the list existed sends. A count of "0 of 0 already
        // applied" would be a claim, and a false one.
        var profile = new ProfileViewModel(new ProfileSummary("old", "d", 9), _ => null);

        Assert.False(profile.HasAppliedText);
        Assert.Equal("", profile.AppliedText);
    }

    [Fact]
    public void A_tweak_this_build_does_not_have_is_never_counted_as_applied()
    {
        var profile = new ProfileViewModel(
            new ProfileSummary("p", "d", 1, ["gone.away"]),
            _ => null);

        Assert.False(Assert.Single(profile.Tweaks).IsApplied);
        Assert.Equal("0 of 1 already applied", profile.AppliedText);
    }

    // --------------------------------------------------------------- grouping

    [Fact]
    public void The_rows_are_gathered_under_their_category_rather_than_repeating_it()
    {
        // The flat list carried the category as a word on every one of forty-two rows, which is
        // a heading pretending to be a column: it said the same thing four times running and
        // gave the list no shape.
        var catalog = new Dictionary<string, TweakItemViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["p1"] = Row("p1", category: TweakCategories.Performance, applied: true),
            ["n1"] = Row("n1", category: TweakCategories.Ping),
            ["p2"] = Row("p2", category: TweakCategories.Performance),
        };

        var profile = new ProfileViewModel(
            new ProfileSummary("p", "d", 3, ["p1", "n1", "p2"]),
            id => catalog.GetValueOrDefault(id));

        var groups = profile.Groups;

        Assert.Equal(2, groups.Count);

        // Sidebar order, not the profile's own and not alphabetical, so a card reads down in the
        // same sequence as the catalog it is drawn from.
        Assert.Equal(["Performance", "Ping"], groups.Select(g => g.Name));
        Assert.Equal(["p1", "p2"], groups[0].Tweaks.Select(t => t.Id));
        Assert.Equal("1/2", groups[0].CountText);
        Assert.Equal("0/1", groups[1].CountText);
    }

    [Fact]
    public void Every_row_survives_the_grouping()
    {
        // The count on the card comes from the profile, so a row lost in the grouping would
        // leave the list shorter than the number above it with nothing to explain the gap.
        var profile = Profile("one", "two", "three");

        Assert.Equal(profile.Tweaks.Count, profile.Groups.Sum(g => g.Tweaks.Count));
    }

    [Fact]
    public void The_arrow_says_which_way_the_card_will_go()
    {
        var profile = Profile("one");

        Assert.Equal("ChevronDownIcon", profile.ToggleGlyphKey);

        profile.Toggle();

        Assert.Equal("ChevronUpIcon", profile.ToggleGlyphKey);
    }
}
