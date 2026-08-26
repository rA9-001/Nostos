using Nostos.Core.Abstractions;

namespace Nostos.Core.Engine;

/// <summary>The catalog. Built once at startup from code-defined and declarative tweaks.</summary>
public sealed class TweakRegistry
{
    private readonly Dictionary<string, ITweak> _byId;

    public TweakRegistry(IEnumerable<ITweak> tweaks)
    {
        _byId = new Dictionary<string, ITweak>(StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in tweaks)
        {
            if (!_byId.TryAdd(tweak.Metadata.Id, tweak))
                throw new InvalidOperationException($"Duplicate tweak id '{tweak.Metadata.Id}'.");
        }
    }

    public IReadOnlyCollection<ITweak> All => _byId.Values;

    public ITweak? Find(string id) => _byId.GetValueOrDefault(id);

    public ITweak Get(string id) => Find(id)
        ?? throw new KeyNotFoundException($"Unknown tweak '{id}'. Run `nos list` to see the catalog.");

    public IEnumerable<ITweak> Query(
        string? category = null,
        Risk maxRisk = Risk.Experimental,
        Evidence weakestEvidence = Evidence.Plausible)
    {
        return All
            .Where(t => category is null || string.Equals(t.Metadata.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Metadata.Risk <= maxRisk)
            // Evidence is ordered best-first (Measured = 0), so "<=" means "at least this
            // trustworthy". With only two values left the default admits everything, which is
            // deliberate: hiding entries was how the old Folklore tier stopped being useful.
            .Where(t => t.Metadata.Evidence <= weakestEvidence)
            // Group first, so the Gaming half is never interleaved with the Windows half, then
            // category order within it -- which is the order a player prioritises rather than
            // A-Z, where the list would open on "Background & Cleanup".
            .OrderBy(t => TweakCategories.GroupOf(t.Metadata.Category))
            .ThenBy(t => TweakCategories.OrderOf(t.Metadata.Category))
            .ThenBy(t => t.Metadata.Id, StringComparer.Ordinal);
    }
}
