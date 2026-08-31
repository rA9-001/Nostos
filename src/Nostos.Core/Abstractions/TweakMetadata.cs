namespace Nostos.Core.Abstractions;

/// <summary>What the tweak touches, which decides who has to be privileged to do it.</summary>
public enum TweakScope
{
    /// <summary>Machine-wide state: HKLM, services, power schemes, drivers.</summary>
    Machine,

    /// <summary>Per-user state: HKCU, per-user shell settings.</summary>
    User,

    /// <summary>A single live process: priority, affinity, QoS.</summary>
    Process,
}

/// <summary>Whether a change outlives the boot that made it.</summary>
public enum TweakLifetime
{
    /// <summary>Survives reboot. Re-verified on every service start.</summary>
    Persistent,

    /// <summary>Evaporates on reboot or process exit. Safe by construction.</summary>
    SessionOnly,
}

/// <summary>How bad it is if this tweak is wrong for the user's machine.</summary>
public enum Risk
{
    /// <summary>Reversible, no boot impact, no hardware interaction.</summary>
    Safe,

    /// <summary>Reversible but user-visible: battery life, background app behaviour.</summary>
    Moderate,

    /// <summary>Can leave the machine unbootable or headless. A restore point is taken first.</summary>
    Risky,

    /// <summary>Unproven and potentially destabilising. Hidden behind an explicit opt-in.</summary>
    Experimental,
}

/// <summary>
/// How much reason there is to believe this tweak does anything.
///
/// Two values, and the field is mandatory. There was a third, <c>Folklore</c>, for changes that
/// are widely repeated with no demonstrated effect; it was removed because it had turned into a
/// label the UI repeated on half the catalog without telling anyone what to do about it. The
/// argument it used to carry has not been dropped -- it lives on each tweak's docs page, which
/// is where somebody deciding whether to apply something actually reads it.
/// </summary>
public enum Evidence
{
    /// <summary>Frametime deltas measured on real hardware, with the data linked from the docs page.</summary>
    Measured,

    /// <summary>Documented mechanism, no frametime data. The docs page says how far that goes.</summary>
    Plausible,
}

/// <summary>Static description of a tweak. Contains no machine state.</summary>
public sealed record TweakMetadata
{
    /// <summary>Stable dotted identifier, e.g. "mmcss.system-responsiveness". Never renamed once shipped.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    /// <summary>
    /// The player-facing outcome this tweak claims to improve, from <see cref="TweakCategories"/>.
    ///
    /// Not the subsystem it touches. Two tweaks that both write HKCU can belong in different
    /// categories, and two that live in different hives can belong in the same one, because the
    /// question the category answers is "what does this do for me", not "where does it write".
    /// </summary>
    public required string Category
    {
        get;

        // Validated on the way in. A category is a promise to the user and the set is closed,
        // so a typo is a bug -- and left unchecked it would surface as a plausible-looking
        // extra bucket in the sidebar holding exactly one tweak, which nobody would question.
        init => field = TweakCategories.Get(value).Id;
    }

    /// <summary>The category record, for anything that needs its label or its promise.</summary>
    public TweakCategory CategoryInfo => TweakCategories.Get(Category);

    public required TweakScope Scope { get; init; }

    /// <summary>
    /// Whether this tweak needs to be told which program it is about.
    ///
    /// Defaults to true for <see cref="TweakScope.Process"/>, which is the obvious case and the
    /// only one there used to be. It is settable because scope and target turn out to be
    /// different questions: <c>process.persistent-priority</c> writes HKLM and outlives every
    /// process it affects, so it is machine-scoped, yet it still has to be pointed at an
    /// executable. Deciding from the scope alone left it with no way to be told which one.
    /// </summary>
    public bool TakesTargetProcess
    {
        get => _takesTargetProcess ?? Scope == TweakScope.Process;
        init => _takesTargetProcess = value;
    }

    private readonly bool? _takesTargetProcess;

    public required TweakLifetime Lifetime { get; init; }

    public required Risk Risk { get; init; }

    public required Evidence Evidence { get; init; }

    public bool RequiresReboot { get; init; }

    public bool RequiresElevation { get; init; } = true;

    /// <summary>Repo-relative docs page. CI fails if this file does not exist.</summary>
    public string DocsPath => $"docs/tweaks/{Id}.md";

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Tweak ids that must not be applied alongside this one.</summary>
    public IReadOnlyList<string> ConflictsWith { get; init; } = [];

    /// <summary>
    /// Settings the user picks between, each explained. Empty for tweaks that do exactly one
    /// thing, which is most of them. See <see cref="TweakChoice"/> for when to add one.
    /// </summary>
    public IReadOnlyList<TweakChoice> Choices { get; init; } = [];

    /// <summary>Resolves every declared choice against a set of selections, applying defaults.</summary>
    public IReadOnlyDictionary<string, TweakChoiceOption> ResolveChoices(
        IReadOnlyDictionary<string, string> selections)
    {
        var resolved = new Dictionary<string, TweakChoiceOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in Choices)
        {
            selections.TryGetValue(choice.Id, out var selected);
            resolved[choice.Id] = choice.Resolve(selected);
        }
        return resolved;
    }
}
