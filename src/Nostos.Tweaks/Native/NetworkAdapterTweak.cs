using System.Text.Json.Nodes;
using Microsoft.Win32;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Native;

/// <summary>
/// Sets one NDIS advanced property on every network adapter that exposes it.
///
/// These are the settings behind the "Advanced" tab of a network adapter's properties. They
/// live in the registry, under the network class key, one subkey per adapter instance:
/// <c>HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-...}\0002</c>. Native rather than
/// declarative for the same reason as <see cref="TcpLatencyTweak"/>: the instance number
/// differs per machine and changes when adapters are added or removed, so there is no path
/// that can be written down in the catalog.
///
/// Two details that are easy to get wrong and expensive to get wrong:
///
/// <para><b>The values are REG_SZ, not REG_DWORD.</b> Every NDIS keyword is a string, even the
/// ones that are obviously numbers. A DWORD written here is ignored by some drivers and rejected
/// by others, and in both cases the setting silently does not apply while the registry looks
/// correct.</para>
///
/// <para><b>A keyword that is absent is never created.</b> Which properties exist depends on the
/// driver, and a keyword the driver has never heard of is not a setting that is off -- it is a
/// setting that does not exist. Writing one leaves junk in the adapter's config that survives
/// this program being uninstalled, and can make a driver refuse to load its configuration.
/// Absent means skipped, everywhere: in Read, in Apply and in Applicability.</para>
///
/// This design also makes vendor differences work themselves out. Realtek spells the
/// energy-saving feature <c>EnableGreenEthernet</c>, Intel spells it <c>*EEE</c>; a tweak names
/// both, and each adapter gets the ones it actually has.
/// </summary>
public sealed class NetworkAdapterTweak : ITweak
{
    /// <summary>The device class every network adapter is registered under. Fixed by Windows.</summary>
    private const string AdapterClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private readonly IReadOnlyList<string> _keywords;
    private readonly string _target;
    private readonly string _absentReason;

    public NetworkAdapterTweak(
        string id,
        string title,
        string summary,
        IReadOnlyList<string> keywords,
        string target,
        string absentReason,
        IReadOnlyList<string>? tags = null)
    {
        _keywords = keywords;
        _target = target;
        _absentReason = absentReason;

        Metadata = new TweakMetadata
        {
            Id = id,
            Title = title,
            Summary = summary,
            Category = TweakCategories.Ping,
            Scope = TweakScope.Machine,
            Lifetime = TweakLifetime.Persistent,
            Risk = Risk.Moderate,
            Evidence = Evidence.Plausible,
            // The driver reads its configuration when the adapter initialises. Disabling and
            // re-enabling the adapter would also do it, and would drop the connection under
            // whoever is using it -- so this asks for a reboot rather than doing that.
            RequiresReboot = true,
            RequiresElevation = true,
            Tags = tags ?? ["network", "adapter"],
        };
    }

    public TweakMetadata Metadata { get; }

    /// <summary>Adapter instance subkeys: the four-digit ones, which is all of them.</summary>
    private static IReadOnlyList<string> AdapterPaths()
    {
        using var baseKey = RegistryAccess.OpenBase("HKLM");
        using var adapters = baseKey.OpenSubKey(AdapterClassKey, writable: false);

        if (adapters is null)
            return [];

        return
        [
            .. adapters.GetSubKeyNames()
                .Where(name => name.Length == 4 && name.All(char.IsAsciiDigit))
                .Select(name => $@"{AdapterClassKey}\{name}"),
        ];
    }

    /// <summary>
    /// Every (adapter, keyword) pair that exists right now.
    ///
    /// This is the self-selecting part: the WAN Miniports, the Kernel Debug Network Adapter and
    /// the other pseudo-adapters that share the class key do not carry these keywords, so they
    /// drop out without needing a list of what counts as a real NIC.
    /// </summary>
    private IReadOnlyList<RegistryValueRef> Targets()
    {
        var targets = new List<RegistryValueRef>();

        using var baseKey = RegistryAccess.OpenBase("HKLM");
        foreach (var path in AdapterPaths())
        {
            using var adapter = baseKey.OpenSubKey(path, writable: false);
            if (adapter is null)
                continue;

            var present = adapter.GetValueNames();
            foreach (var keyword in _keywords)
            {
                if (present.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                    targets.Add(new RegistryValueRef("HKLM", path, keyword));
            }
        }

        return targets;
    }

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(Targets().Count == 0
            // Keyed on the tweak's own id: each of these names the setting it went looking for,
            // so there is no shared sentence to share a key with.
            ? Applicability.No($"notapplicable.{Metadata.Id}", _absentReason)
            : Applicability.Applicable);

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        var targets = Targets();
        if (targets.Count == 0)
            return Task.FromResult(TweakState.Unknown(_absentReason));

        var set = targets.Count(t =>
            RegistryAccess.Read(t).Value is string current
            && string.Equals(current.Trim(), _target, StringComparison.OrdinalIgnoreCase));

        // Partially applied is not applied. One adapter tweaked and one not is the state this
        // exists to avoid: the machine's latency then depends on which cable is plugged in.
        return Task.FromResult(new TweakState(
            set == targets.Count,
            $"{set} of {targets.Count} adapter setting(s) at {_target}"));
    }

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        var captured = new JsonArray();
        foreach (var target in Targets())
            captured.Add((JsonNode?)RegistryAccess.Capture(target));

        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject { ["values"] = captured }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        foreach (var target in Targets())
        {
            ct.ThrowIfCancellationRequested();
            RegistryAccess.Write(target, _target, RegistryValueKind.String);
            context.Log.Debug($"{Metadata.Id}: set {target} = {_target}");
        }

        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        if (snapshot.Data["values"] is not JsonArray values)
            throw new InvalidDataException($"{Metadata.Id}: snapshot has no 'values' array.");

        // Restore everything captured even if an adapter has since been removed and its key is
        // gone. A missing key is not a failure: there is nothing left to put back.
        Exception? firstError = null;
        foreach (var node in values)
        {
            if (node is not JsonObject entry)
                continue;

            try
            {
                RegistryAccess.Restore(entry);
            }
            catch (Exception e)
            {
                firstError ??= e;
                context.Log.Error($"{Metadata.Id}: could not restore a value", e);
            }
        }

        if (firstError is not null)
            throw firstError;

        return Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => (await ReadAsync(context, ct).ConfigureAwait(false)).IsApplied;
}
