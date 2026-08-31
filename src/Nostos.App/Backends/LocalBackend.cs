using Nostos.Core.Localization;
using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Journal;
using Nostos.Core.Profiles;
using Nostos.Core.Safety;
using Nostos.Ipc;
using Nostos.Tweaks;
using Nostos.Win32.Services;

namespace Nostos.App.Backends;

/// <summary>
/// Drives the engine in-process, for when the service is not installed.
///
/// Everything still works, with one difference the UI states plainly: machine-scope tweaks are
/// skipped unless the app itself was started elevated. The app never *requests* elevation —
/// installing the service is the supported way to get it, and it is a one-time prompt rather
/// than one per launch.
/// </summary>
public sealed class LocalBackend : ILocalBackend
{
    private readonly TweakEngine _engine;
    private readonly IJournal _journal;

    public LocalBackend(ILogSink? log = null)
    {
        AppPaths.EnsureCreated();
        var sink = log ?? NullLogSink.Instance;

        _journal = new JsonlJournal(AppPaths.JournalPath, sink);

        _engine = new TweakEngine(
            CatalogFactory.CreateRegistry(),
            _journal,
            WindowsPrivilegeCheck.Instance,
            new SystemRestoreSafetyGate(sink),
            sink);

        CanApplyMachineScope = WindowsPrivilegeCheck.Instance.IsElevated;
    }

    public string Description => CanApplyMachineScope
        ? Strings.Get("connection.direct.elevated")
        : Strings.Get("connection.direct.plain");

    public bool IsService => false;

    /// <summary>
    /// Runs engine work on a thread-pool thread and comes back.
    ///
    /// Every leaf operation in the catalog is synchronous inside -- a registry write, an SCM
    /// call, a powercfg invocation -- wrapped in an already-completed Task. Awaiting one of
    /// those does <em>not</em> yield: the continuation runs inline on whichever thread called
    /// it, which from the app is the UI thread. So the window froze for the duration of every
    /// apply, revert and refresh, and the busy indicator that was supposed to be showing could
    /// not paint because the thread that would have painted it was doing the work.
    ///
    /// The service path never had this problem, because a named pipe is genuinely asynchronous.
    /// That is why it only ever showed up in portable mode and on user-scoped tweaks -- the two
    /// cases that run in-process.
    ///
    /// This is the one place it needs fixing: it is the boundary where the app stops talking to
    /// something remote and starts doing the work itself.
    /// </summary>
    private static Task<T> OffUiThread<T>(Func<Task<T>> work, CancellationToken ct)
        => Task.Run(work, ct);

    public bool CanApplyMachineScope { get; }

    /// <summary>
    /// Scope of a tweak, or null when the id is unknown.
    ///
    /// Exposed for <see cref="SplitBackend"/>, which decides from this whether a tweak has to be
    /// done in-process because it lives in the calling user's hive.
    /// </summary>
    public TweakScope? ScopeOf(string tweakId) => _engine.Registry.Find(tweakId)?.Metadata.Scope;

    /// <summary>
    /// The selections in a named profile, in order.
    ///
    /// Also for <see cref="SplitBackend"/>: applying a profile across two backends means
    /// knowing what is in it before deciding where each entry goes.
    /// </summary>
    public IReadOnlyList<TweakSelection> ProfileSelections(string name)
        => ProfileLoader.LoadDirectory(AppPaths.ProfilesDirectory)
               .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
               ?.Tweaks
           ?? throw new KeyNotFoundException($"No profile named '{name}'.");

    public async Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(CancellationToken ct = default)
    {
        var statuses = await OffUiThread(
            () => _engine.GetStatusAsync(ct: ct), ct).ConfigureAwait(false);

        return [.. statuses.Select(Summarise)];
    }

    public async Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        TweakTarget? target = null,
        CancellationToken ct = default)
    {
        var tweak = _engine.Registry.Find(tweakId);
        if (tweak is null)
            return null;

        var context = new TweakContext
        {
            Options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            TargetProcessId = target?.ProcessId,
            TargetProcessName = target?.ProcessName,
        };

        var statuses = await OffUiThread(
            () => _engine.GetStatusAsync([tweak], context, ct), ct).ConfigureAwait(false);

        return statuses.Select(Summarise).FirstOrDefault();
    }

    private static TweakStatusSummary Summarise(TweakStatus s) => new(
        new TweakSummary(
            s.Metadata.Id, s.Metadata.Title, s.Metadata.Summary, s.Metadata.Category,
            s.Metadata.Scope, s.Metadata.Lifetime, s.Metadata.Risk, s.Metadata.Evidence,
            s.Metadata.RequiresReboot, s.Metadata.RequiresElevation, s.Metadata.Choices,
            s.Metadata.TakesTargetProcess, s.Metadata.Tags),
        s.State.IsApplied,
        s.IsManagedByUs,
        s.State.Description,
        s.Applicability.IsApplicable,
        s.Applicability.Reason,
        s.Applicability.ReasonKey,
        s.Applicability.ReasonArgs);

    public async Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        TweakTarget? target = null,
        CancellationToken ct = default)
    {
        var context = new TweakContext
        {
            Options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DryRun = dryRun,
            TargetProcessId = target?.ProcessId,
            TargetProcessName = target?.ProcessName,
        };

        return Flatten(await OffUiThread(
            () => _engine.ApplyManyAsync([new TweakSelection(tweakId, options)], context, "gui", ct: ct),
            ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ChangeResult>> RevertAsync(string tweakId, CancellationToken ct = default)
        => Flatten([await OffUiThread(
            () => _engine.RevertAsync(tweakId, origin: "gui", ct: ct), ct).ConfigureAwait(false)]);

    public async Task<IReadOnlyList<ChangeResult>> RevertAllAsync(CancellationToken ct = default)
        => Flatten(await OffUiThread(
            () => _engine.RevertAllAsync(origin: "gui", ct: ct), ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<JournalLine>> GetJournalAsync(int tail = 60, CancellationToken ct = default)
    {
        var entries = await OffUiThread(() => _journal.ReadAllAsync(ct), ct).ConfigureAwait(false);

        return entries.TakeLast(tail)
            .Select(e => new JournalLine(
                e.TimestampUtc, e.TweakId, e.Action.ToString(), e.Origin, e.Detail, e.Error))
            .ToList();
    }

    public Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(CancellationToken ct = default)
        => OffUiThread<IReadOnlyList<ProfileSummary>>(() => Task.FromResult<IReadOnlyList<ProfileSummary>>(
            [.. ProfileLoader
                .LoadDirectory(AppPaths.ProfilesDirectory)
                .Select(p => new ProfileSummary(
                    p.Name,
                    p.Description,
                    p.Tweaks.Count,
                    [.. p.Tweaks.Select(t => t.TweakId)]))]),
            ct);

    public async Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(
        string name, Func<BatchProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        return Flatten(await OffUiThread(() =>
        {
            // Loading the profile is disk work too, so it goes over with the apply rather than
            // being done on the caller's thread first.
            var profile = ProfileLoader.LoadDirectory(AppPaths.ProfilesDirectory)
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"No profile named '{name}'.");

            return _engine.ApplyManyAsync(
                profile.Tweaks.ToList(), null, $"profile:{profile.Name}", onProgress, ct);
        }, ct).ConfigureAwait(false));
    }

    private static IReadOnlyList<ChangeResult> Flatten(IReadOnlyList<TweakOperationResult> results)
        => results
            .Select(r => new ChangeResult(r.TweakId, r.Outcome, r.Message, r.RequiresReboot))
            .ToList();

    /// <summary>
    /// Switches a startup entry in this process, which is the signed-in user's.
    ///
    /// Per-user entries have to be done here: HKCU inside the LocalSystem service is SYSTEM's
    /// own hive. Machine-wide ones are attempted too and fail honestly if the app is not
    /// elevated, which is the same bargain every machine-scoped tweak makes without a service.
    /// </summary>
    public async Task<StartupSetResult> SetStartupEnabledAsync(
        string id, bool enabled, CancellationToken ct = default)
    {
        if (StartupWire.Find(id) is not { } item)
        {
            return new StartupSetResult(id, false,
                "no startup entry with that id; it may have been uninstalled since the list was read");
        }

        try
        {
            StartupItems.SetEnabled(item, enabled);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return new StartupSetResult(id, false, e.Message);
        }

        // Recorded only after the write succeeded. The history is a record of what happened to
        // the machine, not of what was attempted -- a refused write leaves the entry exactly as
        // it was, and a line saying otherwise would be the one kind of lie this log must not
        // contain.
        await StartupJournal.RecordAsync(_journal, item.Id, item.Name, enabled, ct).ConfigureAwait(false);

        return new StartupSetResult(id, true, enabled ? "enabled" : "disabled");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
