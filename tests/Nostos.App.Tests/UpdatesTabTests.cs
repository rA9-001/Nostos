using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;
using Nostos.Core.Localization;
using Nostos.Ipc;
using Nostos.Tweaks;
using Nostos.Tweaks.Declarative;

namespace Nostos.App.Tests;

/// <summary>
/// The Windows Update tab.
///
/// It is a second way into tweaks that already exist, not a second copy of them. That is the
/// thing worth holding: the rows are the same view models the Tweaks tab shows, so the two
/// pages cannot drift into disagreeing about whether something is on.
///
/// Membership comes from the "windows-update" tag rather than a category, because these tweaks
/// deliberately sit in three different categories -- a driver swap is Crashes and Freezes, a
/// restart toast is Interruptions, a background download is Ping. A category is a claim about
/// what a tweak does for the player, and "which part of Windows it writes to" is not one.
/// </summary>
public sealed class UpdatesTabTests : IDisposable
{
    public void Dispose() => Strings.Language = Language.English;

    private static async Task<MainWindowViewModel> WindowAsync(params TweakStatusSummary[] tweaks)
    {
        var backend = new FakeBackend();
        backend.Statuses.AddRange(tweaks);

        var viewModel = new MainWindowViewModel(backend);
        await viewModel.InitialiseAsync();
        return viewModel;
    }

    private static TweakStatusSummary Update(
        string id, bool applied = false, Risk risk = Risk.Safe, bool applicable = true)
        => FakeBackend.Tweak(id, TweakCategories.Interruptions, risk,
            applied: applied, applicable: applicable, tags: ["windows-update"]);

    [Fact]
    public async Task Only_tweaks_carrying_the_tag_are_on_the_tab()
    {
        var window = await WindowAsync(
            Update("update.active-hours"),
            FakeBackend.Tweak("mmcss.system-responsiveness"),
            Update("update.no-restart-notifications"));

        Assert.Equal(
            ["update.active-hours", "update.no-restart-notifications"],
            window.UpdateTweaks.Select(t => t.Id));
    }

    [Fact]
    public async Task A_row_on_the_tab_is_the_same_object_the_catalog_tab_shows()
    {
        // Not a copy. Two view models over one tweak is how a page ends up saying ON while the
        // page next to it says OFF, and nothing on screen would explain which was right.
        var window = await WindowAsync(Update("update.active-hours"));

        var onTheTab = Assert.Single(window.UpdateTweaks);
        var inTheCatalog = window.Tweaks.Single(t => t.Id == "update.active-hours");

        Assert.Same(inTheCatalog, onTheTab);
    }

    [Fact]
    public async Task The_gentlest_tweaks_are_listed_first()
    {
        // The page is read top to bottom by somebody deciding, so the ones with the least to
        // lose come first and the Moderate one is not the first thing they are offered.
        var window = await WindowAsync(
            Update("update.pin-windows-version", risk: Risk.Moderate),
            Update("update.active-hours"));

        Assert.Equal(
            ["update.active-hours", "update.pin-windows-version"],
            window.UpdateTweaks.Select(t => t.Id));
    }

    [Fact]
    public async Task The_count_says_how_many_are_on_rather_than_how_many_exist()
    {
        var window = await WindowAsync(
            Update("update.active-hours", applied: true),
            Update("update.no-restart-notifications"),
            Update("update.store-auto-download-off"));

        Assert.Equal("1 of 3 on", window.UpdateSummary);
    }

    [Fact]
    public async Task An_inapplicable_tweak_is_never_counted_as_on()
    {
        // A tweak can be both inapplicable and report itself applied -- a Pro-only policy whose
        // value is sitting in the registry of a Home machine is exactly that. Counting it would
        // make the chip claim something about this PC that is not true.
        var window = await WindowAsync(Update("update.pin-windows-version", applied: true, applicable: false));

        Assert.Equal("0 of 1 on", window.UpdateSummary);
    }

    [Fact]
    public async Task Clicking_a_row_that_is_off_applies_it()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(Update("update.active-hours"));
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        await window.ToggleTweakCommand.ExecuteAsync(window.UpdateTweaks[0]);

        Assert.Equal(["update.active-hours"], backend.Applied);
        Assert.Empty(backend.Reverted);
    }

    [Fact]
    public async Task Clicking_a_row_that_is_on_reverts_it()
    {
        // Revert, not "apply the opposite". These are real tweaks with captured prior values,
        // and putting the captured value back is the only undo that restores what was there
        // rather than what this program guesses the default to be.
        var backend = new FakeBackend();
        backend.Statuses.Add(Update("update.active-hours", applied: true));
        var window = new MainWindowViewModel(backend);
        await window.InitialiseAsync();

        await window.ToggleTweakCommand.ExecuteAsync(window.UpdateTweaks[0]);

        Assert.Equal(["update.active-hours"], backend.Reverted);
        Assert.Empty(backend.Applied);
    }

    [Fact]
    public async Task The_switch_crossfades_rather_than_blinking()
    {
        var window = await WindowAsync(
            Update("update.active-hours", applied: true),
            Update("update.store-auto-download-off"));

        var on = window.UpdateTweaks.Single(t => t.Id == "update.active-hours");
        var off = window.UpdateTweaks.Single(t => t.Id == "update.store-auto-download-off");

        Assert.Equal(1, on.OnOpacity);
        Assert.Equal(0, on.OffOpacity);
        Assert.Equal(0, off.OnOpacity);
        Assert.Equal(1, off.OffOpacity);
        Assert.True(off.RowOpacity < on.RowOpacity);
    }

    [Fact]
    public async Task A_row_says_what_clicking_it_will_do()
    {
        var window = await WindowAsync(
            Update("update.active-hours", applied: true),
            Update("update.store-auto-download-off"));

        Assert.Contains("undo", window.UpdateTweaks.Single(t => t.Id == "update.active-hours").ToggleHint,
            StringComparison.Ordinal);
        Assert.Contains("apply", window.UpdateTweaks.Single(t => t.Id == "update.store-auto-download-off").ToggleHint,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_is_translated()
    {
        var window = await WindowAsync(Update("update.active-hours"));

        Strings.Language = Language.German;

        Assert.Equal("0 von 1 aktiv", window.UpdateSummary);
    }

    /// <summary>
    /// The tag is the contract between the catalog and the tab, so it is worth checking that it
    /// actually selects the tweaks a reader would expect to find there -- and, more usefully,
    /// that nothing about Windows Update was left off the page by forgetting the tag.
    /// </summary>
    [Fact]
    public void The_real_catalog_puts_the_update_tweaks_on_the_tab()
    {
        var tagged = CatalogFactory.CreateAll()
            .Where(t => t.Metadata.Tags.Contains("windows-update", StringComparer.OrdinalIgnoreCase))
            .Select(t => t.Metadata.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every tweak whose id says it is about Windows Update has to be on the page. The
        // reverse does not hold: a service, and the driver tweak, belong there under their own
        // names without an "update." prefix.
        var byName = CatalogFactory.CreateAll()
            .Select(t => t.Metadata.Id)
            .Where(id => id.StartsWith("update.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(byName);
        Assert.All(byName, id => Assert.Contains(id, tagged));

        // And the two that would be easy to miss, because their ids say something else.
        Assert.Contains("stability.driver-search-off", tagged);
        Assert.Contains("services.delivery-optimization", tagged);
    }

    /// <summary>
    /// Two tweaks writing the same registry value is a trap the journal cannot dig anyone out
    /// of: apply both, revert one, and the other's value is silently undone while its row still
    /// reads ON. This tab makes it likelier by putting related tweaks side by side, so the rule
    /// is checked here.
    /// </summary>
    [Fact]
    public void No_two_tweaks_on_the_page_write_the_same_registry_value()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clashes = new List<string>();

        foreach (var definition in RegistryTweakCatalog.LoadEmbedded()
                     .Where(d => d.Tags.Contains("windows-update", StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var value in definition.AllReachableValues)
            {
                var key = $"{value.Hive}|{value.Key}|{value.Name}";
                if (owners.TryGetValue(key, out var first))
                    clashes.Add($"{key} is written by both {first} and {definition.Id}");
                else
                    owners[key] = definition.Id;
            }
        }

        Assert.True(clashes.Count == 0, string.Join(Environment.NewLine, clashes));
    }
}
