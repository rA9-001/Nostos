namespace Nostos.Core.Engine;

/// <summary>A tweak plus the parameters to apply it with. What a profile is made of.</summary>
/// <param name="TweakId">Catalog id.</param>
/// <param name="Options">Tweak-specific parameters, e.g. {"value": "10"}.</param>
public sealed record TweakSelection(string TweakId, IReadOnlyDictionary<string, string>? Options = null)
{
    public IReadOnlyDictionary<string, string> EffectiveOptions
        => Options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
