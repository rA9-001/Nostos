using Nostos.Core.Localization;
using Nostos.App.Localization;
using Nostos.Core.Abstractions;

namespace Nostos.App.ViewModels;

/// <summary>
/// Which rows the catalog shows, in what order, and under which band heading.
///
/// Split out of the window's view model because none of it is view-model work: it is a pure
/// function from (every tweak, a category, a search string) to a list. The window keeps the
/// part that genuinely needs UI state -- reconciling that list into a bound collection without
/// throwing away the user's selection and scroll position.
/// </summary>
internal static class CatalogFilter
{
    public const string AllCategories = "all";

    /// <summary>
    /// A filter, not a category.
    ///
    /// Whether a tweak applies here is live machine state -- a service that is not installed, a
    /// Windows build that never had the setting, no game running to point at -- and it differs
    /// between two PCs holding the same catalog. Making it a real <see cref="TweakCategory"/>
    /// would mean a docs page could not name the category it is filed under, a profile could
    /// reference a bucket that is empty on the next machine, and CI could not check either. So
    /// it lives here, alongside "all", derived from what the last refresh actually found.
    /// </summary>
    public const string NotApplicableCategory = "not-applicable";

    /// <summary>The band printed above the unavailable rows, and shown when they are filtered to.</summary>
    ///
    /// <remarks>
    /// A property rather than a constant, because it is text the reader sees and therefore
    /// changes with the language. The English lives in the string table with the rest of the
    /// interface; only the category id above it is a constant, because that one is a name the
    /// program uses to talk to itself.
    /// </remarks>
    public static string NotApplicableHeader => Strings.Get("tweaks.na.header");

    public static string NotApplicableDescription => Strings.Get("tweaks.na.description");

    /// <summary>
    /// What the selected category claims about the tweaks inside it, or null for "all".
    ///
    /// Shown under the filter list so the heading is not left to carry the meaning on its own:
    /// "Ping" is a promise, and the user is entitled to see it spelled out before deciding that
    /// six rows under it are worth applying.
    /// </summary>
    public static string? PromiseOf(string category) => category switch
    {
        AllCategories => null,
        NotApplicableCategory => NotApplicableDescription,
        _ => CatalogText.CategoryPromise(category),
    };

    /// <summary>
    /// The categories worth offering, in reading order.
    ///
    /// Grouped first, so the sidebar reads Gaming-then-Windows and the band headings the item
    /// template draws land where they belong. "Not applicable" is appended only when something
    /// is in it: an always-present filter that is usually empty trains people to ignore it, and
    /// on a machine where everything applies it is a question with no answer.
    /// </summary>
    public static List<string> CategoriesFor(IReadOnlyList<TweakItemViewModel> all)
    {
        var desired = all
            .Select(t => t.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(TweakCategories.GroupOf)
            .ThenBy(TweakCategories.OrderOf)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (all.Any(t => !t.IsApplicable))
            desired.Add(NotApplicableCategory);

        return desired;
    }

    /// <summary>
    /// The rows to show, ordered, with each section's first row carrying its band heading.
    ///
    /// Nothing is filtered out by how well evidenced it is. Every tweak in the catalog is
    /// listed, always; a user who had heard of one and could not find it concluded the tool
    /// lacked it, and went looking for a .reg file instead.
    /// </summary>
    public static List<TweakItemViewModel> Select(
        IReadOnlyList<TweakItemViewModel> all, string category, string? search)
    {
        var filtered = all
            .Where(t => category switch
            {
                AllCategories => true,
                NotApplicableCategory => !t.IsApplicable,
                _ => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase),
            })
            .Where(t => t.Matches(search))
            // Unavailable rows sink to the bottom whatever else is selected. They are not
            // choices, and leaving them interleaved means every scan of the list steps over
            // things that cannot be clicked.
            .OrderBy(t => t.IsApplicable ? 0 : 1)
            // Group before category, so the Gaming half is never interleaved with the Windows
            // half. Turning off the Fax service and extending the GPU timeout are both
            // reasonable things to do and have nothing to do with each other.
            .ThenBy(t => TweakCategories.GroupOf(t.Category))
            .ThenBy(t => TweakCategories.OrderOf(t.Category))
            // Safest first, and the enum is declared in that order: Safe, Moderate, Risky,
            // Experimental. Applies everywhere, not only where the band headings are drawn, so
            // that scrolling any list means moving from what you can click without thinking
            // towards what you should read first. Alphabetical order put "Disable the Windows
            // Error Reporting service" above "Extend the GPU hang timeout" for no reason a
            // reader could see.
            .ThenBy(t => t.Risk)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        AssignBands(filtered, category);
        return filtered;
    }

    /// <summary>
    /// Puts each section's heading on its first row, at both levels.
    ///
    /// Cleared on every other row, or a row that used to lead a section keeps its heading after
    /// something above it appears.
    /// </summary>
    private static void AssignBands(List<TweakItemViewModel> filtered, string category)
    {
        string? previousSuper = null;
        string? previous = null;

        foreach (var tweak in filtered)
        {
            var (superHeader, superDescription) = OuterSectionOf(tweak, category);
            var leadsSuper = superHeader != previousSuper;

            tweak.SuperHeader = leadsSuper ? superHeader : null;
            tweak.SuperDescription = leadsSuper ? superDescription : null;
            previousSuper = superHeader;

            var (header, description) = SectionOf(tweak, category);

            // A row that opens an outer band always opens an inner one too, even when the
            // inner heading has not changed. Without this, filtering to a search term that
            // spans both halves prints "Windows" with the first category underneath it
            // unlabelled, because that category happened to be the last one under "Gaming".
            var leads = leadsSuper || header != previous;

            tweak.GroupHeader = leads ? header : null;
            tweak.GroupDescription = leads ? description : null;
            previous = header;
        }
    }

    /// <summary>
    /// The outer band a row sits under, or null where there is only one level.
    ///
    /// Only the unfiltered list has two things to say: which half of the catalog you are
    /// looking at, and which category within it. Inside one category the sidebar has already
    /// answered the first, and the unavailable pile is not in a half at all.
    /// </summary>
    private static (string? Header, string? Description) OuterSectionOf(
        TweakItemViewModel tweak, string category)
    {
        if (!tweak.IsApplicable || category is not AllCategories)
            return (null, null);

        var group = TweakCategories.GroupOf(tweak.Category);
        return (CatalogText.GroupName(group), CatalogText.GroupDescription(group));
    }

    /// <summary>
    /// Which band a row sits under.
    ///
    /// Applicability wins over everything. A tweak that cannot run here is not a Gaming change
    /// or a safe change from the reader's point of view -- it is not a change at all.
    ///
    /// After that it depends on what the reader has already told us. Inside one category, every
    /// row is there for the same reason -- everything under Ping is there to improve ping -- so
    /// the question still open is what a change costs if it goes wrong, and the bands are the
    /// risk levels, safest first. Across the whole catalog the category is not settled yet, so
    /// it is the band, and the half of the catalog moves up to the outer one.
    /// </summary>
    private static (string Header, string Description) SectionOf(
        TweakItemViewModel tweak, string category)
    {
        if (!tweak.IsApplicable)
            return (NotApplicableHeader, NotApplicableDescription);

        if (category is not (AllCategories or NotApplicableCategory))
            return (CatalogText.RiskBandName(tweak.Risk), CatalogText.RiskBandDescription(tweak.Risk));

        return (CatalogText.CategoryName(tweak.Category),
                CategoryPromiseOf(tweak.Category));
    }

    /// <summary>The promise under a category band, or "" for a category the catalog forgot.</summary>
    private static string CategoryPromiseOf(string category)
        => CatalogText.CategoryPromise(category) ?? "";
}
