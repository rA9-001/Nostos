namespace Nostos.Core.Abstractions;

/// <summary>
/// One setting on a tweak where several values are all defensible, and which one is right
/// depends on the machine and on what the user is optimising for.
///
/// The existence of this type is a judgement about honesty. A tweak with a single hardcoded
/// value implies there is a correct answer; for something like "how much CPU should the
/// scheduler reserve for background work", there is not. Rather than picking one and hiding the
/// trade-off, the tweak declares the options and says what each one costs.
///
/// Every option carries its own <see cref="TweakChoiceOption.Description"/> because a list of
/// bare values ("0, 10, 20") is a quiz, not a choice. The UI shows the description next to the
/// option, and the CLI prints it under `nos show &lt;id&gt;`.
/// </summary>
public sealed record TweakChoice
{
    /// <summary>
    /// Key this choice is passed under, e.g. "level". Matches <see cref="TweakContext.Options"/>
    /// and the `--set level=...` CLI flag. Stable once shipped: profiles store it.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Short label, e.g. "Reservation level".</summary>
    public required string Title { get; init; }

    /// <summary>What this setting controls, in a sentence.</summary>
    public required string Description { get; init; }

    /// <summary>Id of the option used when the user has not chosen one.</summary>
    public required string DefaultOption { get; init; }

    public required IReadOnlyList<TweakChoiceOption> Options { get; init => field = value ?? []; }

    /// <summary>
    /// Picks the option for a selection, falling back to the default.
    ///
    /// An unrecognised selection throws rather than silently falling back: it means a profile
    /// or a command line named an option that no longer exists, and quietly applying something
    /// else is exactly the behaviour this project exists to avoid.
    /// </summary>
    public TweakChoiceOption Resolve(string? selected)
    {
        if (string.IsNullOrWhiteSpace(selected))
            return Find(DefaultOption) ?? throw new InvalidOperationException(
                $"Choice '{Id}' has no option named '{DefaultOption}' to use as its default.");

        return Find(selected) ?? throw new ArgumentException(
            $"'{selected}' is not a valid setting for '{Id}'. Valid: {string.Join(", ", Options.Select(o => o.Id))}.");
    }

    public TweakChoiceOption? Find(string id)
        => Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One selectable value of a <see cref="TweakChoice"/>.</summary>
public sealed record TweakChoiceOption
{
    /// <summary>Stable identifier, e.g. "balanced". Stored in profiles; never renamed.</summary>
    public required string Id { get; init; }

    /// <summary>Label including the concrete value where one exists, e.g. "Balanced — 10%".</summary>
    public required string Title { get; init; }

    /// <summary>
    /// What this option actually does and what it costs. Written for someone deciding, so it
    /// says who it suits and what it gives up, not just what it sets.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>Marks the option most people should take. At most one per choice.</summary>
    public bool Recommended { get; init; }
}
