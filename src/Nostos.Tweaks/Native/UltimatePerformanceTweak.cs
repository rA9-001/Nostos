using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Native;

/// <summary>
/// Switches the machine to the Ultimate Performance power scheme, unhiding it first if needed.
///
/// Not expressible declaratively: unhiding the scheme creates state that revert has to clean up,
/// and the GUID the machine ends up with is not always the template GUID.
/// </summary>
public sealed class UltimatePerformanceTweak : ITweak
{
    public TweakMetadata Metadata { get; } = new()
    {
        Id = "power.ultimate-performance",
        Title = "Use the Ultimate Performance power scheme",
        Summary = "High Performance with core parking and idle latency-tolerance disabled. " +
                  "Removes the ramp-up delay when a frame needs a parked core, at the cost of idle power draw.",
        Category = TweakCategories.Performance,
        Scope = TweakScope.Machine,
        Lifetime = TweakLifetime.Persistent,
        Risk = Risk.Moderate,
        Evidence = Evidence.Measured,
        RequiresReboot = false,
        RequiresElevation = true,
        Tags = ["power", "core-parking", "latency"],
    };

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
    {
        if (SystemInfo.HasBattery && !context.GetBool("allowOnBattery", false))
        {
            return Task.FromResult(Applicability.No(
                "this machine has a battery, and Ultimate Performance is a straight battery-life " +
                "regression. Pass allowOnBattery=true if you want it anyway."));
        }

        return Task.FromResult(Applicability.Applicable);
    }

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        var active = PowerSchemes.GetActive();
        var isApplied = active == PowerSchemes.UltimatePerformance;
        return Task.FromResult(new TweakState(
            isApplied, $"active scheme = {PowerSchemes.GetFriendlyName(active)} ({active})"));
    }

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        var active = PowerSchemes.GetActive();
        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject
        {
            ["priorScheme"] = active.ToString(),
            ["priorSchemeName"] = PowerSchemes.GetFriendlyName(active),
            // Records whether the scheme already existed, so revert only deletes a scheme we
            // ourselves unhid and never one the user made.
            ["schemeExistedBefore"] = PowerSchemes.Exists(PowerSchemes.UltimatePerformance),
        }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        var scheme = PowerSchemes.EnsureAvailable(PowerSchemes.UltimatePerformance);
        PowerSchemes.SetActive(scheme);
        context.Log.Info($"{Metadata.Id}: activated {PowerSchemes.GetFriendlyName(scheme)}");
        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        var priorText = snapshot.Data["priorScheme"]?.GetValue<string>();
        if (!Guid.TryParse(priorText, out var prior))
            throw new InvalidDataException($"{Metadata.Id}: snapshot has no usable 'priorScheme'.");

        if (!PowerSchemes.Exists(prior))
        {
            // The scheme the user was on has since been deleted. Balanced is the only value we
            // can be sure exists, and stranding them on Ultimate Performance is worse.
            context.Log.Warn($"{Metadata.Id}: prior scheme {prior} no longer exists, falling back to Balanced");
            prior = PowerSchemes.Balanced;
        }

        PowerSchemes.SetActive(prior);

        if (snapshot.Data["schemeExistedBefore"]?.GetValue<bool>() == false)
        {
            try
            {
                PowerSchemes.Delete(PowerSchemes.UltimatePerformance);
            }
            catch (Exception e)
            {
                // Not fatal: the machine is already back on the user's scheme. Leaving an extra
                // entry in the power menu is untidy, not harmful.
                context.Log.Warn($"{Metadata.Id}: could not remove the unhidden scheme: {e.Message}");
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(PowerSchemes.GetActive() == PowerSchemes.UltimatePerformance);
}
