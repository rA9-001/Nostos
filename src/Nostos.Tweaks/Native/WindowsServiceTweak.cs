using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Native;

/// <summary>
/// Moves one Windows service off Automatic start.
///
/// One tweak per service, deliberately. The alternative -- a single "optimize services" button
/// that rewrites a list of forty -- is what every other tool in this category ships, and it is
/// the reason people end up with broken audio and no idea which of the forty did it. One tweak
/// per service means each one needs its own docs page justifying it, which CI enforces, and
/// each one reverts on its own.
///
/// The default target is <see cref="ServiceStartType.Manual"/>, not Disabled. Manual means the
/// service does not start at boot but still starts if something asks for it: if the reasoning
/// on the docs page turns out to be wrong for a particular machine, the machine quietly works
/// anyway. Disabled means the service cannot start at all, and whatever needed it fails with an
/// error that names neither the service nor this tool. Disabled is offered, because sometimes
/// it is what you want, but it is never the default.
/// </summary>
public sealed class WindowsServiceTweak : ITweak
{
    /// <summary>The choice id every service tweak uses, so profiles read consistently.</summary>
    public const string StartTypeChoice = "start";

    private readonly string _serviceName;

    public WindowsServiceTweak(
        string id,
        string serviceName,
        string title,
        string summary,
        string category,
        Evidence evidence,
        Risk risk = Risk.Moderate,
        IReadOnlyList<string>? tags = null)
    {
        // A tweak that names a protected service is a mistake in the catalog, not a decision to
        // be discovered by a user at apply time. Fail while the catalog is being built.
        if (WindowsServices.ProtectionReason(serviceName) is { } reason)
        {
            throw new ArgumentException(
                $"'{serviceName}' is on the protected list, so it cannot have a tweak. {reason}",
                nameof(serviceName));
        }

        _serviceName = serviceName;

        Metadata = new TweakMetadata
        {
            Id = id,
            Title = title,
            Summary = summary,
            Category = category,
            Scope = TweakScope.Machine,
            Lifetime = TweakLifetime.Persistent,
            Risk = risk,
            Evidence = evidence,
            RequiresReboot = false,
            RequiresElevation = true,
            Tags = tags ?? ["service"],
            Choices =
            [
                new TweakChoice
                {
                    Id = StartTypeChoice,
                    Title = "How the service should start",
                    Description =
                        $"Windows starts {serviceName} automatically at boot. This is what it "
                        + "should do instead.",
                    DefaultOption = "manual",
                    Options =
                    [
                        new TweakChoiceOption
                        {
                            Id = "manual",
                            Title = "Manual - only when something asks",
                            Recommended = true,
                            Description =
                                "The service no longer starts at boot, but Windows can still "
                                + "start it on demand. If it turns out something on this machine "
                                + "needs it, that something keeps working -- it just pays the "
                                + "start-up cost the first time instead of every boot. This is "
                                + "the setting to pick unless you have a specific reason not to.",
                        },
                        new TweakChoiceOption
                        {
                            Id = "disabled",
                            Title = "Disabled - never, by anything",
                            Description =
                                "The service cannot start at all. Nothing can start it, including "
                                + "Windows components that need it, and what they report is a "
                                + "generic failure that names neither the service nor this tool. "
                                + "Pick this only when Manual has been tried and the service is "
                                + "demonstrably still starting itself.",
                        },
                    ],
                },
            ],
        };
    }

    public TweakMetadata Metadata { get; }

    private static ServiceStartType Target(TweakContext context)
        => context.Options.TryGetValue(StartTypeChoice, out var selected)
           && selected.Equals("disabled", StringComparison.OrdinalIgnoreCase)
            ? ServiceStartType.Disabled
            : ServiceStartType.Manual;

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
    {
        // Service names vary by Windows edition and by which optional features are installed,
        // so "not present" is a normal answer rather than an error.
        var info = WindowsServices.Query(_serviceName);
        if (info is null)
        {
            return Task.FromResult(Applicability.No(
                $"'{_serviceName}' is not registered on this machine, so there is nothing to change."));
        }

        if (info.StartType is ServiceStartType.Boot or ServiceStartType.System)
        {
            return Task.FromResult(Applicability.No(
                $"'{_serviceName}' starts at {info.StartType} scope on this machine, which means it "
                + "is loaded as a driver. Changing that can leave the machine unbootable."));
        }

        return Task.FromResult(Applicability.Applicable);
    }

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        var info = WindowsServices.Query(_serviceName);
        if (info is null)
            return Task.FromResult(new TweakState(false, $"{_serviceName} is not installed"));

        var target = Target(context);

        // Disabled satisfies a Manual selection: the user asked for "not at boot", and Disabled
        // is a stricter version of that. The reverse is not true, so a Manual service does not
        // read as applied when Disabled was selected.
        var isApplied = info.StartType == target
                        || (target == ServiceStartType.Manual && info.StartType == ServiceStartType.Disabled);

        var running = info.IsRunning ? "running" : "stopped";
        return Task.FromResult(new TweakState(
            isApplied, $"{_serviceName} start = {Describe(info)}, currently {running}"));
    }

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        var info = WindowsServices.Query(_serviceName)
            ?? throw new InvalidOperationException($"'{_serviceName}' is not registered on this machine.");

        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject
        {
            ["service"] = _serviceName,
            ["priorStartType"] = (int)info.StartType,
            // Automatic and "Automatic (Delayed Start)" are different settings that the SCM
            // reports as the same start type. Restoring the first when the user had the second
            // would change boot behaviour under the cover of a revert.
            ["priorDelayedAutoStart"] = info.DelayedAutoStart,
            ["wasRunning"] = info.IsRunning,
        }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        var target = Target(context);
        WindowsServices.SetStartType(_serviceName, target);
        context.Log.Info($"{Metadata.Id}: {_serviceName} start type set to {target}");

        // Changing the start type does nothing to the copy already running. Stopping it is the
        // difference between "this takes effect now" and "this takes effect at the next boot",
        // and a failure to stop is not a failure to apply.
        if (WindowsServices.TryStop(_serviceName, out var detail))
            context.Log.Info($"{Metadata.Id}: {_serviceName} {detail}");
        else
            context.Log.Warn($"{Metadata.Id}: {_serviceName} {detail}");

        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        if (snapshot.Data["priorStartType"]?.GetValue<int>() is not { } prior)
            throw new InvalidDataException($"{Metadata.Id}: snapshot has no usable 'priorStartType'.");

        var restored = (ServiceStartType)prior;
        WindowsServices.SetStartType(_serviceName, restored);

        if (restored == ServiceStartType.Automatic
            && snapshot.Data["priorDelayedAutoStart"]?.GetValue<bool>() == true)
        {
            WindowsServices.SetDelayedAutoStart(_serviceName, true);
        }

        context.Log.Info($"{Metadata.Id}: {_serviceName} start type restored to {restored}");

        // Deliberately not restarted. The service starts itself on the next boot, which is what
        // its start type means, and starting a service that was stopped for a reason is a
        // bigger intervention than the revert was asked to make.
        return Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => (await ReadAsync(context, ct).ConfigureAwait(false)).IsApplied;

    private static string Describe(ServiceInfo info)
        => info is { StartType: ServiceStartType.Automatic, DelayedAutoStart: true }
            ? "Automatic (Delayed Start)"
            : info.StartType.ToString();
}
