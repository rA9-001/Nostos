using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;
using Microsoft.Win32;

namespace Nostos.Tweaks.Native;

/// <summary>
/// Disables delayed ACK and Nagle coalescing on every network interface.
///
/// Native rather than declarative for one reason: the keys live under a per-interface GUID that
/// differs on every machine and changes when adapters come and go, so the value list cannot be
/// written down in the catalog. Everything else about it is an ordinary registry tweak, and it
/// reuses <see cref="RegistryAccess"/> so capture and revert behave identically.
///
/// Applied to every interface rather than only the one currently carrying traffic. Targeting
/// "the active adapter" sounds tidier and is worse: switching from Wi-Fi to Ethernet, or
/// plugging in a dock, would silently leave the new adapter untweaked while the tweak still
/// reported itself as applied.
/// </summary>
public sealed class TcpLatencyTweak : ITweak
{
    private const string InterfacesKey =
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    /// <summary>Send ACKs immediately instead of waiting for a second segment or the 200 ms timer.</summary>
    private const string AckFrequency = "TcpAckFrequency";

    /// <summary>Disable Nagle: send small writes straight away instead of coalescing them.</summary>
    private const string NoDelay = "TCPNoDelay";

    public TweakMetadata Metadata { get; } = new()
    {
        Id = "network.tcp-latency",
        Title = "Send small TCP packets immediately",
        Summary = "Turns off delayed acknowledgements and Nagle coalescing on every network " +
                  "adapter, so a game's small, frequent TCP writes go out at once instead of " +
                  "waiting to be batched.",
        Category = TweakCategories.Ping,
        Scope = TweakScope.Machine,
        Lifetime = TweakLifetime.Persistent,
        Risk = Risk.Moderate,
        Evidence = Evidence.Plausible,
        // The stack reads these per-connection, but existing connections keep their behaviour,
        // and in practice people expect to reboot before judging it.
        RequiresReboot = true,
        RequiresElevation = true,
        Tags = ["tcp", "nagle", "latency"],
    };

    /// <summary>The interface subkeys present right now, as full paths below HKLM.</summary>
    private static IReadOnlyList<string> InterfacePaths()
    {
        using var baseKey = RegistryAccess.OpenBase("HKLM");
        using var interfaces = baseKey.OpenSubKey(InterfacesKey, writable: false);

        return interfaces is null
            ? []
            : [.. interfaces.GetSubKeyNames().Select(name => $@"{InterfacesKey}\{name}")];
    }

    private static IEnumerable<RegistryValueRef> ValuesFor(string interfacePath)
    {
        yield return new RegistryValueRef("HKLM", interfacePath, AckFrequency);
        yield return new RegistryValueRef("HKLM", interfacePath, NoDelay);
    }

    private static IReadOnlyList<RegistryValueRef> AllValues()
        => [.. InterfacePaths().SelectMany(ValuesFor)];

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
    {
        var interfaces = InterfacePaths();

        return Task.FromResult(interfaces.Count == 0
            ? Applicability.No(
                "notapplicable.nointerfaces",
                "no network interfaces are registered on this machine")
            : Applicability.Applicable);
    }

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        var interfaces = InterfacePaths();
        if (interfaces.Count == 0)
            return Task.FromResult(TweakState.Unknown("no network interfaces found"));

        var set = 0;
        foreach (var interfacePath in interfaces)
        {
            var ack = RegistryAccess.ReadDword(new RegistryValueRef("HKLM", interfacePath, AckFrequency));
            var nodelay = RegistryAccess.ReadDword(new RegistryValueRef("HKLM", interfacePath, NoDelay));

            if (ack == 1 && nodelay == 1)
                set++;
        }

        // Partially applied counts as not applied. A machine with one adapter tweaked and one
        // not is exactly the state this tweak exists to avoid, so Verify must fail it.
        return Task.FromResult(new TweakState(
            set == interfaces.Count,
            $"{set} of {interfaces.Count} adapter(s) set to send immediately"));
    }

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        var captured = new JsonArray();
        foreach (var reference in AllValues())
            captured.Add((JsonNode?)RegistryAccess.Capture(reference));

        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject { ["values"] = captured }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        foreach (var reference in AllValues())
        {
            ct.ThrowIfCancellationRequested();
            RegistryAccess.Write(reference, 1, RegistryValueKind.DWord);
            context.Log.Debug($"{Metadata.Id}: set {reference} = 1");
        }

        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        if (snapshot.Data["values"] is not JsonArray values)
            throw new InvalidDataException($"{Metadata.Id}: snapshot has no 'values' array.");

        // Restore everything captured, even if one adapter has since been removed and its key
        // is gone. A missing key is not a failure here: there is nothing left to put back.
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
