using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Journal;
using Nostos.Core.Profiles;
using Nostos.Core.Safety;
using Nostos.Ipc;
using Nostos.Tweaks;
using Nostos.Win32.ServiceControl;
using Nostos.Win32.Services;

namespace Nostos.Service.Daemon;

/// <summary>
/// Everything the service actually does.
///
/// Two concurrent jobs, cancelled together: serve the control pipe, and periodically re-apply
/// tweaks that Windows has quietly reset. Nothing here knows about the Service Control Manager,
/// which is what lets the whole daemon run in the foreground under <c>--console</c> for
/// development.
///
/// Nothing here undoes a change on its own, either. There is no timer waiting for the user to
/// confirm that the machine still works.
///
/// Note what is NOT here: nothing enumerates processes, and nothing reacts to a game starting.
/// This service is idle while you play. See docs/architecture.md.
/// </summary>
public sealed class OptimizerDaemon
{
    private readonly ServiceConfiguration _configuration;
    private readonly ILogSink _log;
    private readonly TweakEngine _engine;
    private readonly IJournal _journal;

    public OptimizerDaemon(ServiceConfiguration configuration, ILogSink log)
    {
        _configuration = configuration;
        _log = log;

        AppPaths.EnsureCreated();
        _journal = new JsonlJournal(AppPaths.JournalPath, log);

        _engine = new TweakEngine(
            CatalogFactory.CreateRegistry(),
            _journal,
            WindowsPrivilegeCheck.Instance,
            new SystemRestoreSafetyGate(log),
            log);
    }

    private static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// True when running as LocalSystem, in which case HKCU writes would land in SYSTEM's own
    /// hive instead of the user's. User-scoped tweaks are refused rather than silently misapplied.
    /// </summary>
    private static bool RunningAsSystem
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _log.Info($"starting v{Version}, catalog has {_engine.Registry.All.Count} tweaks");
        _log.Info($"running as {WindowsIdentity.GetCurrent().Name}");

        var jobs = new List<Task>
        {
            new ControlPipeServer(_configuration, _log, HandleAsync).RunAsync(ct),
            ReconcileLoopAsync(ct),
        };

        try
        {
            await Task.WhenAll(jobs).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }

        _log.Info("stopped");
    }

    // ------------------------------------------------------------- background

    private async Task ReconcileLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(_configuration.ReconcileMinutes, 1, 24 * 60));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var results = await _engine.ReconcileAsync(ct: ct).ConfigureAwait(false);
                foreach (var result in results)
                    _log.Warn($"reconcile: {result.TweakId} had drifted, re-applied: {result.Message}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                _log.Error("reconcile failed", e);
            }

            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static IReadOnlyList<TweakProfile> LoadProfiles()
    {
        try
        {
            return ProfileLoader.LoadDirectory(AppPaths.ProfilesDirectory);
        }
        catch (Exception)
        {
            // A malformed profile must not stop the service; the IPC 'profiles' command
            // surfaces the parse error to the user instead.
            return [];
        }
    }

    // -------------------------------------------------------------- IPC

    private async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        return request.Command switch
        {
            IpcCommands.Ping => IpcResponse.Success(request.Id, await PingAsync(ct).ConfigureAwait(false)),
            IpcCommands.List => IpcResponse.Success(request.Id, ListTweaks()),
            IpcCommands.Status => IpcResponse.Success(
                request.Id, await StatusAsync(request.PayloadAs<ChangeRequest>(), ct).ConfigureAwait(false)),
            IpcCommands.Apply => IpcResponse.Success(
                request.Id, await ApplyAsync(Required<ChangeRequest>(request), ct).ConfigureAwait(false)),
            IpcCommands.Revert => IpcResponse.Success(
                request.Id, await RevertAsync(Required<ChangeRequest>(request), ct).ConfigureAwait(false)),
            IpcCommands.Journal => IpcResponse.Success(
                request.Id, await JournalAsync(request.PayloadAs<JournalRequest>(), ct).ConfigureAwait(false)),
            IpcCommands.Reconcile => IpcResponse.Success(
                request.Id, Flatten(await _engine.ReconcileAsync(ct: ct).ConfigureAwait(false))),
            IpcCommands.Profiles => IpcResponse.Success(request.Id, ListProfiles()),
            IpcCommands.ApplyProfile => IpcResponse.Success(
                request.Id, await ApplyProfileAsync(Required<ApplyProfileRequest>(request), ct).ConfigureAwait(false)),
            _ => IpcResponse.Failure(request.Id, $"unknown command '{request.Command}'"),
        };
    }

    private static T Required<T>(IpcRequest request) where T : class
        => request.PayloadAs<T>()
           ?? throw new InvalidDataException($"'{request.Command}' requires a payload.");

    private async Task<PingResult> PingAsync(CancellationToken ct) => new(
        IpcContract.ProtocolVersion,
        Version,
        Environment.ProcessId,
        _engine.Registry.All.Count,
        (await _journal.GetOutstandingAsync(ct).ConfigureAwait(false)).Count);

    private List<TweakSummary> ListTweaks()
        => _engine.Registry.All.Select(Summarise).ToList();

    private static TweakSummary Summarise(ITweak tweak)
    {
        var m = tweak.Metadata;
        return new TweakSummary(
            m.Id, m.Title, m.Summary, m.Category, m.Scope, m.Lifetime,
            m.Risk, m.Evidence, m.RequiresReboot, m.RequiresElevation, m.Choices);
    }

    private async Task<List<TweakStatusSummary>> StatusAsync(ChangeRequest? request, CancellationToken ct)
    {
        var subset = request?.TweakIds is { Count: > 0 } ids
            ? ids.Select(_engine.Registry.Get).ToList()
            : null;

        var statuses = await _engine
            .GetStatusAsync(subset, ToContext(request), ct)
            .ConfigureAwait(false);

        return statuses.Select(s => new TweakStatusSummary(
            new TweakSummary(
                s.Metadata.Id, s.Metadata.Title, s.Metadata.Summary, s.Metadata.Category,
                s.Metadata.Scope, s.Metadata.Lifetime, s.Metadata.Risk, s.Metadata.Evidence,
                s.Metadata.RequiresReboot, s.Metadata.RequiresElevation, s.Metadata.Choices),
            s.State.IsApplied,
            s.IsManagedByUs,
            s.State.Description,
            s.Applicability.IsApplicable,
            s.Applicability.Reason)).ToList();
    }

    private async Task<List<ChangeResult>> ApplyAsync(ChangeRequest request, CancellationToken ct)
    {
        var refusals = RefuseUserScoped(request.TweakIds, out var allowed);

        var selections = allowed
            .Select(id => new TweakSelection(id, request.Options))
            .ToList();

        var results = selections.Count == 0
            ? []
            : await _engine.ApplyManyAsync(selections, ToContext(request), request.Origin, ct)
                .ConfigureAwait(false);

        return [.. refusals, .. Flatten(results)];
    }

    private async Task<List<ChangeResult>> RevertAsync(ChangeRequest request, CancellationToken ct)
    {
        if (request.All)
        {
            var all = await _engine.RevertAllAsync(ToContext(request), request.Origin, ct)
                .ConfigureAwait(false);
            return Flatten(all);
        }

        var results = new List<TweakOperationResult>();
        foreach (var id in request.TweakIds)
        {
            results.Add(await _engine
                .RevertAsync(id, ToContext(request), request.Origin, ct)
                .ConfigureAwait(false));
        }

        return Flatten(results);
    }

    /// <summary>
    /// Rejects user-scoped tweaks when running as LocalSystem.
    ///
    /// Writing HKCU from SYSTEM writes to SYSTEM's own hive: the change would appear to succeed,
    /// verify would even pass, and the user's setting would be untouched. Refusing with a clear
    /// message is the only honest option until impersonation of the console session is
    /// implemented.
    /// </summary>
    private List<ChangeResult> RefuseUserScoped(
        IReadOnlyList<string> requested, out List<string> allowed)
    {
        allowed = [];
        var refusals = new List<ChangeResult>();

        foreach (var id in requested)
        {
            var tweak = _engine.Registry.Find(id);
            if (tweak is not null && RunningAsSystem && tweak.Metadata.Scope == TweakScope.User)
            {
                refusals.Add(new ChangeResult(id, Outcome.Skipped,
                    "user-scoped tweak cannot be applied by the LocalSystem service; " +
                    "run it from the CLI as the signed-in user instead", false));
                continue;
            }

            allowed.Add(id);
        }

        return refusals;
    }

    private async Task<List<ChangeResult>> ApplyProfileAsync(ApplyProfileRequest request, CancellationToken ct)
    {
        var profile = LoadProfiles()
            .FirstOrDefault(p => string.Equals(p.Name, request.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"No profile named '{request.Name}' in {AppPaths.ProfilesDirectory}.");

        var change = new ChangeRequest
        {
            TweakIds = profile.Tweaks.Select(s => s.TweakId).ToList(),
            DryRun = request.DryRun,
            Origin = $"profile:{profile.Name}",
        };

        var refusals = RefuseUserScoped(change.TweakIds, out var allowed);

        var selections = profile.Tweaks
            .Where(s => allowed.Contains(s.TweakId))
            .ToList();

        var results = selections.Count == 0
            ? []
            : await _engine.ApplyManyAsync(selections, ToContext(change), change.Origin, ct)
                .ConfigureAwait(false);

        return [.. refusals, .. Flatten(results)];
    }

    private async Task<List<JournalLine>> JournalAsync(JournalRequest? request, CancellationToken ct)
    {
        var tail = Math.Clamp(request?.Tail ?? 30, 1, 1000);
        var entries = await _journal.ReadAllAsync(ct).ConfigureAwait(false);

        return entries.TakeLast(tail)
            .Select(e => new JournalLine(
                e.TimestampUtc, e.TweakId, e.Action.ToString(), e.Origin, e.Detail, e.Error))
            .ToList();
    }

    private List<ProfileSummary> ListProfiles()
        => ProfileLoader.LoadDirectory(AppPaths.ProfilesDirectory)
            .Select(p => new ProfileSummary(p.Name, p.Description, p.Tweaks.Count))
            .ToList();

    private TweakContext ToContext(ChangeRequest? request) => new()
    {
        Options = request?.Options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        TargetProcessId = request?.TargetProcessId,
        TargetProcessName = request?.TargetProcessName,
        DryRun = request?.DryRun ?? false,
        Log = _log,
    };

    private static List<ChangeResult> Flatten(IReadOnlyList<TweakOperationResult> results)
        => results
            .Select(r => new ChangeResult(r.TweakId, r.Outcome, r.Message, r.RequiresReboot))
            .ToList();
}
