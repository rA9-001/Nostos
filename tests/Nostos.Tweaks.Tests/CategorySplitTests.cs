using Nostos.Core.Abstractions;
using Nostos.Tweaks;
using Xunit;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// The line between "Unused Features" and "Background Services".
///
/// These were one category. It was called Unused Features and it held twenty tweaks, every one
/// of which was a service -- so the name described nothing. There was no distinction being
/// drawn, only a label implying one, and a reader scrolling twenty near-identical rows had no
/// way to tell which of them they were qualified to have an opinion about.
///
/// The line now is exactly that: **who is qualified to decide**. A tweak in `unused` names
/// something the reader recognises -- Bluetooth, printing, Xbox, Fax -- and can settle in a
/// second from facts about their own life that this program has no access to. A tweak in
/// `services` names something almost nobody has heard of, where the reader has no basis for an
/// opinion and the tweak has to supply one.
///
/// That distinction is a judgement and cannot be asserted directly. What can be asserted is the
/// consequence each promise claims, and that is what these tests hold: a `services` page has to
/// explain what the service is for, and an `unused` page has to say what stops working.
/// </summary>
public sealed class CategorySplitTests
{
    private static readonly IReadOnlyList<TweakMetadata> All =
        [.. CatalogFactory.CreateAll().Select(t => t.Metadata)];

    private static IEnumerable<TweakMetadata> In(string category)
        => All.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>Walks up from the test binary to the repo root, so this works from any CWD.</summary>
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nostos.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    private static string DocsOf(TweakMetadata tweak)
        => File.ReadAllText(Path.Combine(RepoRoot, "docs", "tweaks", $"{tweak.Id}.md"));

    [Fact]
    public void Both_halves_of_the_old_bucket_have_tweaks_in_them()
    {
        // A split that leaves one side nearly empty has not divided anything; it has added a
        // heading. Deliberately not asserting a balance -- the point is that both are real.
        Assert.True(In(TweakCategories.Unused).Count() >= 5);
        Assert.True(In(TweakCategories.Services).Count() >= 5);
    }

    [Fact]
    public void A_background_service_explains_what_the_service_is_actually_for()
    {
        // What the category's promise claims: "these have no name you would recognise, so each
        // one says what it is actually for". Somebody deciding about TrkWks has nothing to go on
        // but the page, so the page has to carry the mechanism.
        foreach (var tweak in In(TweakCategories.Services))
        {
            Assert.True(
                DocsOf(tweak).Contains("## Mechanism", StringComparison.Ordinal),
                $"{tweak.Id} is filed under Background Services, whose promise is that each tweak "
                + "says what the service is actually for, but its docs page has no ## Mechanism "
                + "section. Nobody has heard of this service; the page is all they get.");
        }
    }

    [Fact]
    public void An_unused_feature_names_what_stops_working()
    {
        // The other half of the bargain: these are things the reader decides for themselves, and
        // they can only do that if the page says what they would be giving up.
        foreach (var tweak in In(TweakCategories.Unused))
        {
            Assert.True(
                DocsOf(tweak).Contains("## Trade-off", StringComparison.Ordinal),
                $"{tweak.Id} is filed under Unused Features, whose promise is that each one names "
                + "what stops working, but its docs page has no ## Trade-off section.");
        }
    }

    [Fact]
    public void Xbox_is_filed_with_the_other_features_rather_than_alone()
    {
        // Xbox had a category of its own, on the argument that the four services are one
        // decision rather than four. True -- and equally true of printing, of Bluetooth and of
        // Fax, none of which got a sidebar entry. "Do you use Game Pass?" is the same shape of
        // question as "do you have a printer?", so it belongs in the same list.
        var xbox = All.Where(t => t.Id.StartsWith("services.xbox-", StringComparison.Ordinal)).ToList();

        Assert.Equal(4, xbox.Count);
        Assert.All(xbox, t => Assert.Equal(TweakCategories.Unused, t.Category));
    }

    [Fact]
    public void There_is_no_xbox_category_left_to_drift_back_into()
    {
        Assert.Null(TweakCategories.Find("xbox"));
    }

    [Fact]
    public void The_two_promises_are_not_the_same_claim_reworded()
    {
        // The failure this whole change was fixing. If the two promises ever converge, the split
        // has stopped meaning anything again and the second heading is just noise.
        var features = TweakCategories.Get(TweakCategories.Unused);
        var services = TweakCategories.Get(TweakCategories.Services);

        Assert.NotEqual(features.Promise, services.Promise);
        Assert.NotEqual(features.Name, services.Name);

        // Each promise has to carry the half of the distinction it is responsible for: the
        // features list says the reader already knows, the services list says it will explain.
        Assert.Contains("You already know", features.Promise, StringComparison.Ordinal);
        Assert.Contains("no name you would recognise", services.Promise, StringComparison.Ordinal);
    }

    [Fact]
    public void Neither_bucket_promises_frames()
    {
        // Both are honest about being cleanup rather than performance, and that sentence is the
        // reason the catalog is allowed to offer the Fax service at all.
        Assert.Contains(
            "does not, on its own, promise you frames",
            TweakCategories.Get(TweakCategories.Services).Promise,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "FPS", TweakCategories.Get(TweakCategories.Unused).Promise, StringComparison.Ordinal);
    }
}
