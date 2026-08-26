namespace Nostos.Core.Abstractions;

/// <summary>
/// Which half of the catalog a category belongs to.
///
/// The split exists because the two halves answer different questions. "Will this help me in a
/// match?" and "do I need this part of Windows at all?" are both worth asking, but mixing the
/// answers is what made the old single list confusing: the Fax service and the GPU timeout
/// delay sat under the same heading, and neither reader was served.
/// </summary>
public enum TweakGroup
{
    /// <summary>Changes with a mechanism that reaches the game: frames, latency, faults.</summary>
    Gaming,

    /// <summary>Changes to Windows itself. Worth doing, but not because of a game.</summary>
    Windows,
}

/// <summary>
/// One of the fixed buckets a tweak can be filed under.
///
/// The categories name what a person notices, not what a Windows subsystem is called. "cpu"
/// and "shell" describe the code that gets touched; nobody has ever sat down at a machine
/// wanting more shell. They want more frames, fewer hitches, a lower ping, and to not be
/// dragged out of a match by a toast.
/// </summary>
public sealed record TweakCategory
{
    /// <summary>Stable slug used on the wire, in profiles and in <c>nos list --category</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Short label for the UI. Gaming vocabulary, not Windows vocabulary.</summary>
    public required string Name { get; init; }

    /// <summary>Whether this bucket claims to help a game, or only to tidy up Windows.</summary>
    public required TweakGroup Group { get; init; }

    /// <summary>
    /// What filing a tweak here claims about it. One sentence a maintainer can be held to when
    /// reviewing a pull request.
    /// </summary>
    public required string Promise { get; init; }

    /// <summary>
    /// Display order. Deliberately not alphabetical -- the list reads top to bottom in the
    /// order a player cares, so the first thing they see is the thing they came for.
    /// </summary>
    public required int Order { get; init; }

    /// <summary>
    /// Other words people use for this outcome, for the search box only.
    ///
    /// Somebody hunting for stutter fixes types "hitching", "frametime" or "lag spikes"; one
    /// after frames types "framerate" or just "performance". None of those appear in a tweak's
    /// title, and an empty result list reads as "this tool does not do that".
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>True if a search box entry should surface tweaks in this category.</summary>
    public bool Matches(string search)
        => Id.Contains(search, StringComparison.OrdinalIgnoreCase)
           || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
           || Keywords.Any(k => k.Contains(search, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The closed set of categories. Adding one is a deliberate act: a category is a claim, so a
/// new bucket needs a promise that can be checked, not just a convenient home for a tweak that
/// does not fit anywhere.
/// </summary>
public static class TweakCategories
{
    // Gaming.
    public const string Performance = "performance";
    public const string InputLag = "input-lag";
    public const string Ping = "ping";
    public const string Stability = "stability";

    // Windows.
    public const string Interruptions = "interruptions";
    public const string Background = "background";

    public static IReadOnlyList<TweakCategory> All { get; } =
    [
        new()
        {
            Id = Performance,
            Group = TweakGroup.Gaming,
            Keywords =
            [
                "fps", "framerate", "frames per second", "frame rate", "performance", "speed",
                "1% lows", "hitching", "frametime", "micro-stutter", "stutter", "lag spikes", "smoothness",
            ],
            Name = "Performance",
            Order = 0,
            // Frames and frametimes were two categories until it became clear nobody was
            // choosing between them: a tweak that frees CPU raises the average *and* fills in
            // the dips, and splitting it forced a coin-flip at filing time that the reader
            // then had to guess at.
            Promise = "Raises the framerate and evens out frametimes, by giving the game CPU, "
                    + "GPU or memory that Windows was spending on something else.",
        },
        new()
        {
            Id = InputLag,
            Group = TweakGroup.Gaming,
            Keywords = ["mouse", "aim", "responsiveness", "click to photon", "sensitivity", "keyboard", "latency"],
            Name = "Input Lag & Aim",
            Order = 1,
            Promise = "Shortens or steadies the path from your mouse and keyboard to the "
                    + "screen, so the same movement always produces the same result.",
        },
        new()
        {
            Id = Ping,
            Group = TweakGroup.Gaming,
            Keywords = ["latency", "netcode", "lag", "network", "packet loss", "rubber-banding"],
            Name = "Ping",
            Order = 2,
            Promise = "Cuts round-trip network latency, or stops Windows adding delay of its "
                    + "own to traffic the game is waiting on.",
        },
        new()
        {
            Id = Stability,
            Group = TweakGroup.Gaming,
            Keywords = ["crash", "freeze", "flicker", "black screen", "driver timeout", "bluescreen", "tdr"],
            Name = "Crashes & Freezes",
            Order = 3,
            Promise = "Fixes a specific fault that shows up while playing: driver timeouts, "
                    + "black screens, flicker. Repairs a broken machine rather than making a "
                    + "working one faster.",
        },
        new()
        {
            Id = Interruptions,
            Group = TweakGroup.Windows,
            Keywords = ["popup", "toast", "notification", "overlay", "alt-tab", "focus", "windows update"],
            Name = "Interruptions",
            Order = 4,
            Promise = "Stops things appearing over what you are doing, stealing focus, or "
                    + "restarting the machine at a moment you did not choose.",
        },
        new()
        {
            Id = Background,
            Group = TweakGroup.Windows,
            Keywords =
            [
                "services", "startup", "boot", "bloat", "debloat", "cleanup", "telemetry",
                "privacy", "disk", "memory", "ram",
            ],
            Name = "Background & Cleanup",
            Order = 5,
            // The honest home for most service tweaks. Turning off the Fax service is a
            // reasonable thing to do and a dishonest thing to sell as a framerate tweak, and
            // this bucket is what lets the catalog offer it without making that claim.
            Promise = "Stops Windows running services and features you do not use. Frees some "
                    + "memory and boot time; does not, on its own, promise you frames.",
        },
    ];

    private static readonly Dictionary<string, TweakCategory> ById =
        All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>The categories in one group, in display order.</summary>
    public static IEnumerable<TweakCategory> InGroup(TweakGroup group)
        => All.Where(c => c.Group == group).OrderBy(c => c.Order);

    /// <summary>Heading for a group, as the UI and the CLI both print it.</summary>
    public static string NameOfGroup(TweakGroup group) => group switch
    {
        TweakGroup.Gaming => "Gaming",
        TweakGroup.Windows => "Windows",
        _ => group.ToString(),
    };

    /// <summary>One line saying what the group is for, printed under its heading.</summary>
    public static string DescriptionOfGroup(TweakGroup group) => group switch
    {
        TweakGroup.Gaming => "Changes with a mechanism that reaches the game.",
        TweakGroup.Windows => "Changes to Windows itself. Worth doing, but not because of a game.",
        _ => "",
    };

    public static TweakCategory? Find(string? id)
        => id is not null && ById.TryGetValue(id, out var category) ? category : null;

    /// <summary>
    /// Looks a category up, failing loudly. A typo'd category would otherwise show up as an
    /// extra one-tweak bucket in the sidebar, which is the kind of thing nobody notices.
    /// </summary>
    public static TweakCategory Get(string id)
        => Find(id) ?? throw new ArgumentOutOfRangeException(
            nameof(id),
            id,
            $"Unknown tweak category '{id}'. Categories are a closed set: {string.Join(", ", All.Select(c => c.Id))}. "
            + "See docs/architecture.md for what each one promises.");

    /// <summary>Display label, falling back to the raw id so an unknown category still renders.</summary>
    public static string NameOf(string id) => Find(id)?.Name ?? id;

    /// <summary>Sort key. Unknown categories sort last rather than throwing inside a comparer.</summary>
    public static int OrderOf(string id) => Find(id)?.Order ?? int.MaxValue;

    /// <summary>Which half of the catalog a tweak's category belongs to.</summary>
    public static TweakGroup GroupOf(string id) => Find(id)?.Group ?? TweakGroup.Windows;
}
