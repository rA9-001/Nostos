using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Tweaks.Declarative;
using Nostos.Tweaks.Native;

namespace Nostos.Tweaks;

/// <summary>
/// Assembles the full catalog.
///
/// Almost all of it is data: registry tweaks, service tweaks and per-adapter network settings
/// are JSON files under Catalog\, embedded in this assembly. What is left here is the handful
/// of tweaks that genuinely need code -- because they call an API, or because their revert is
/// something other than putting an old registry value back.
///
/// That split is the contributor surface. Adding a tweak should be a JSON object and a docs
/// page; needing to write a class is the signal that a tweak is doing something unusual, and
/// the friction is deliberate.
/// </summary>
public static class CatalogFactory
{
    public static IReadOnlyList<ITweak> CreateAll()
    {
        var tweaks = new List<ITweak>();

        foreach (var definition in RegistryTweakCatalog.LoadEmbedded())
            tweaks.Add(new RegistryTweak(definition));

        foreach (var definition in ServiceTweakCatalog.LoadEmbedded())
            tweaks.Add(definition.ToTweak());

        foreach (var definition in AdapterTweakCatalog.LoadEmbedded())
            tweaks.Add(definition.ToTweak());

        // The four that are not data. Each one reverts to something it had to go and read at
        // apply time: a power scheme GUID, a process's previous priority class, every permanent
        // image priority on the machine, the TCP parameters of whichever interface is carrying
        // the default route.
        tweaks.Add(new UltimatePerformanceTweak());
        tweaks.Add(new GameProcessTuningTweak());
        tweaks.Add(new ImagePriorityTweak());
        tweaks.Add(new TcpLatencyTweak());

        return tweaks;
    }

    public static TweakRegistry CreateRegistry() => new(CreateAll());
}
