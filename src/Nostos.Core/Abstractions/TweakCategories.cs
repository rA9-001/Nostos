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
    public const string Telemetry = "telemetry";
    public const string Startup = "startup";
    public const string Unused = "unused";
    public const string Services = "services";
    public const string Storage = "storage";

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
            // "FPS", not "framerate": it is what this audience says in both languages, and the
            // German table is held to the same glossary. See StringTableTests.
            Promise = "Raises the FPS and evens out frametimes, by giving the game CPU, "
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
        // "Background & Cleanup" used to be all five of the categories below, and held 38 of
        // the 84 tweaks -- nearly half the catalog under one heading, thirty of them the same
        // sentence with a different service name in it. A bucket that large is not a category,
        // it is the absence of one: nothing in it could be found except by reading all of it,
        // and its promise had to be vague enough to cover the Fax service and NTFS timestamps
        // at once. Each of these makes a claim narrow enough to be worth reading.
        new()
        {
            Id = Telemetry,
            Group = TweakGroup.Windows,
            Keywords =
            [
                "telemetry", "privacy", "tracking", "diagnostics", "data collection", "spying",
                "advertising", "location", "activity history",
            ],
            Name = "Telemetry & Privacy",
            Order = 5,
            Promise = "Stops Windows collecting, identifying and uploading what you do. Nothing "
                    + "here is about speed, and none of it is a substitute for the privacy "
                    + "settings Windows already offers.",
        },
        new()
        {
            Id = Startup,
            Group = TweakGroup.Windows,
            Keywords = ["startup", "boot", "start-up", "login", "sign-in", "prefetch", "superfetch", "indexing"],
            Name = "Startup & Boot",
            Order = 6,
            Promise = "Shortens the gap between signing in and a machine that is actually ready, "
                    + "by removing work Windows schedules for itself in those first minutes.",
        },
        // These two were one bucket, and the bucket was called Unused Features while holding
        // twenty tweaks that were all services. So the label described nothing: there was no
        // distinction being drawn, only a name that implied one.
        //
        // The line between them is who is qualified to decide. Everything in Unused Features
        // names something the reader recognises -- Bluetooth, printing, Xbox, Fax -- and can
        // therefore settle in a second, from facts about their own life that this program has
        // no access to. Everything in Services names something almost nobody has heard of, where
        // the reader has no basis for an opinion and the tweak has to supply one.
        //
        // That is a real difference in how the two lists get read, which is what a category is
        // for. "Is it a service?" would not have been: they are all services.
        new()
        {
            Id = Unused,
            Group = TweakGroup.Windows,
            Keywords =
            [
                "unused", "bloat", "debloat", "cleanup", "fax", "bluetooth", "printer",
                "smart card", "nfc", "sensors", "xbox", "game pass", "game bar", "gamebar",
            ],
            Name = "Unused Features",
            Order = 7,
            // The Xbox services live here rather than in a category of their own. They were
            // separated on the argument that they are one decision rather than four, which is
            // true -- and it is equally true of printing, of Bluetooth and of Fax. "Do you use
            // Game Pass?" is the same shape of question as "do you have a printer?", so it
            // belongs in the same list as those, not in a fourth sidebar entry of its own.
            Promise = "Turns off features this PC has but you do not use -- Bluetooth, printing, "
                    + "Xbox and Game Pass, Fax. You already know which of these you need; each "
                    + "one names what stops working.",
        },
        new()
        {
            Id = Services,
            Group = TweakGroup.Windows,
            Keywords =
            [
                "services", "background", "netbios", "alljoyn", "remote registry",
                "link tracking", "offline files", "backup", "plumbing",
            ],
            Name = "Background Services",
            Order = 8,
            // The honest home for a service with no user-visible feature attached. Turning off
            // Distributed Link Tracking is a reasonable thing to do and a dishonest thing to
            // sell as a framerate tweak, and this bucket is what lets the catalog offer it
            // without making that claim.
            Promise = "Stops Windows services that run for nothing you use. Unlike the features "
                    + "above, these have no name you would recognise, so each one says what it "
                    + "is actually for. Frees a little memory and boot time; does not, on its "
                    + "own, promise you frames.",
        },
        new()
        {
            Id = Storage,
            Group = TweakGroup.Windows,
            Keywords = ["disk", "ssd", "hdd", "ntfs", "filesystem", "file system", "storage", "writes"],
            Name = "Disk & Filesystem",
            Order = 9,
            Promise = "Removes bookkeeping NTFS does on every file operation for the sake of "
                    + "software that is no longer here.",
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
