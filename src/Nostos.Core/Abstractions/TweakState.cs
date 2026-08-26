using System.Text.Json.Nodes;

namespace Nostos.Core.Abstractions;

/// <summary>The live value of a tweak on this machine, read fresh.</summary>
/// <param name="IsApplied">True when the machine already matches what Apply would do.</param>
/// <param name="Description">Human-readable current value, e.g. "SystemResponsiveness = 20 (Windows default)".</param>
/// <param name="Raw">Machine-readable current value, for the UI and for tests.</param>
public sealed record TweakState(bool IsApplied, string Description, JsonObject? Raw = null)
{
    public static TweakState Unknown(string why) => new(false, why);
}

/// <summary>
/// The prior value of everything a tweak is about to touch, captured immediately before Apply.
///
/// Revert restores <em>this</em>, never a hardcoded "Windows default" — that assumption is what
/// makes other optimizers destroy machines whose OEM shipped a non-default value.
/// </summary>
/// <param name="TweakId">Owning tweak.</param>
/// <param name="CapturedUtc">When the prior value was read.</param>
/// <param name="Data">Tweak-defined payload. Opaque to the engine, round-tripped through the journal.</param>
public sealed record TweakSnapshot(string TweakId, DateTimeOffset CapturedUtc, JsonObject Data)
{
    public static TweakSnapshot Create(string tweakId, JsonObject data)
        => new(tweakId, DateTimeOffset.UtcNow, data);
}
