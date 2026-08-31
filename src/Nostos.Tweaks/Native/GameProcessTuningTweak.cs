using System.Globalization;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Native;

/// <summary>
/// Tunes a single running process: scheduling priority and EcoQoS participation.
///
/// Applied on demand against a process you name; nothing looks for one on its own. It touches
/// no stored configuration, dies with the process,
/// and can be applied and undone while a match is in progress. It also never opens the target
/// for memory access, so it stays clear of anti-cheat.
/// </summary>
public sealed class GameProcessTuningTweak : ITweak
{
    public TweakMetadata Metadata { get; } = new()
    {
        Id = "process.game-tuning",
        Title = "Prioritise a running game process",
        Summary = "Raises the process priority class and opts the process out of EcoQoS, so the " +
                  "scheduler stops treating it as background-efficiency work.",
        Category = TweakCategories.Performance,
        Scope = TweakScope.Process,
        Lifetime = TweakLifetime.SessionOnly,
        Risk = Risk.Safe,
        Evidence = Evidence.Plausible,
        RequiresReboot = false,
        // Works unelevated against a process the same user launched, which is the common case.
        RequiresElevation = false,
        Tags = ["live", "priority", "qos"],
        Choices =
        [
            new TweakChoice
            {
                Id = "priority",
                Title = "Scheduling priority",
                Description =
                    "Where the game sits in the scheduler's queue relative to everything else " +
                    "running. Higher means it is picked first when threads compete for a core.",
                DefaultOption = "high",
                Options =
                [
                    new TweakChoiceOption
                    {
                        Id = "abovenormal",
                        Title = "Above normal",
                        Description =
                            "A gentle nudge ahead of ordinary background work. Safe on any " +
                            "machine, including ones where you keep working while a game runs.",
                    },
                    new TweakChoiceOption
                    {
                        Id = "high",
                        Title = "High",
                        Description =
                            "Ahead of essentially everything except the system itself. The right " +
                            "pick on a machine dedicated to playing, and the usual recommendation.",
                        Recommended = true,
                    },
                ],
            },
            new TweakChoice
            {
                Id = "qos",
                Title = "Power throttling",
                Description =
                    "Whether Windows may run the game on efficiency cores and at reduced clocks " +
                    "to save power. Modern Windows applies this automatically to processes it " +
                    "judges to be background work.",
                DefaultOption = "high",
                Options =
                [
                    new TweakChoiceOption
                    {
                        Id = "high",
                        Title = "Never throttle",
                        Description =
                            "Opts the process out of EcoQoS entirely, so it keeps full clocks and " +
                            "performance cores. What you want on a desktop, and while plugged in.",
                        Recommended = true,
                    },
                    new TweakChoiceOption
                    {
                        Id = "system",
                        Title = "Let Windows decide",
                        Description =
                            "Leaves throttling under the system's own control. Sensible on a " +
                            "laptop on battery, where an unthrottled game costs runtime and heat.",
                    },
                    new TweakChoiceOption
                    {
                        Id = "efficiency",
                        Title = "Always throttle",
                        Description =
                            "Forces the process onto efficiency cores and reduced clocks. This " +
                            "makes games slower, deliberately. Useful only for pinning a " +
                            "background process out of the way.",
                    },
                ],
            },
        ],
    };

    private static ProcessPriorityClass DesiredPriority(TweakContext context)
    {
        // Accepts either a declared option id ("high") or a raw ProcessPriorityClass name, so
        // profiles written before the choices existed keep working.
        var requested = context.GetString("priority") ?? "high";

        if (string.Equals(requested, nameof(ProcessPriorityClass.RealTime), StringComparison.OrdinalIgnoreCase))
        {
            // Realtime outranks input, audio and the mouse cursor. A game that saturates the CPU
            // at realtime priority can make the machine unresponsive to the point of needing a
            // hard reset, and it has never been shown to help frametimes.
            throw new ArgumentException(
                "Realtime priority is deliberately not supported: it starves input and audio " +
                "and can hang the desktop. Use High.");
        }

        return Enum.TryParse<ProcessPriorityClass>(requested, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown priority class '{requested}'.");
    }

    private static QosMode DesiredQos(TweakContext context)
        => context.GetString("qos")?.ToLowerInvariant() switch
        {
            "efficiency" => QosMode.Efficiency,
            "system" or "systemmanaged" => QosMode.SystemManaged,
            _ => QosMode.HighPerformance,
        };

    private static int RequirePid(TweakContext context)
        => context.TargetProcessId
           ?? throw new InvalidOperationException("No target process; pass --pid or --process.");

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
    {
        if (context.TargetProcessId is not { } pid)
            return Task.FromResult(Applicability.No(
                "notapplicable.nopid", "no target process specified"));

        return Task.FromResult(ProcessControl.IsRunning(pid)
            ? Applicability.Applicable
            : Applicability.No(
                "notapplicable.processgone",
                $"process {pid} is not running",
                pid.ToString(CultureInfo.InvariantCulture)));
    }

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        var pid = RequirePid(context);
        var priority = ProcessControl.GetPriority(pid);
        var qos = ProcessControl.GetQos(pid);

        var isApplied = priority == DesiredPriority(context) && qos == DesiredQos(context);
        return Task.FromResult(new TweakState(isApplied, $"pid {pid}: priority = {priority}, qos = {qos}"));
    }

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        var pid = RequirePid(context);
        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject
        {
            ["pid"] = pid,
            ["processName"] = context.TargetProcessName,
            ["priority"] = ProcessControl.GetPriority(pid).ToString(),
            ["qos"] = ProcessControl.GetQos(pid).ToString(),
        }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        var pid = RequirePid(context);
        ProcessControl.SetPriority(pid, DesiredPriority(context));
        ProcessControl.SetQos(pid, DesiredQos(context));
        context.Log.Info($"{Metadata.Id}: pid {pid} -> {DesiredPriority(context)} / {DesiredQos(context)}");
        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        var pid = snapshot.Data["pid"]?.GetValue<int>()
            ?? throw new InvalidDataException($"{Metadata.Id}: snapshot has no 'pid'.");

        // The process exiting is the normal way this tweak ends. That is not a failure, and it
        // must not leave an un-revertible entry sitting in the journal forever.
        if (!ProcessControl.IsRunning(pid))
        {
            context.Log.Debug($"{Metadata.Id}: pid {pid} already exited; nothing to restore");
            return Task.CompletedTask;
        }

        if (Enum.TryParse<ProcessPriorityClass>(snapshot.Data["priority"]?.GetValue<string>(), out var priority))
            ProcessControl.SetPriority(pid, priority);

        if (Enum.TryParse<QosMode>(snapshot.Data["qos"]?.GetValue<string>(), out var qos))
            ProcessControl.SetQos(pid, qos);

        return Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => (await ReadAsync(context, ct).ConfigureAwait(false)).IsApplied;
}
