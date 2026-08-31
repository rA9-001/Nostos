using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;
using Nostos.Win32.Services;

namespace Nostos.App.Backends;

/// <summary>
/// Sends each tweak to whichever half of the program can actually do it.
///
/// The service runs as LocalSystem, which is what machine-scope tweaks need — but SYSTEM has
/// its own user hive, so a user-scoped (HKCU) tweak sent to the service reads and writes
/// SYSTEM's settings, not yours. Writes were already refused with an explanation. Reads were
/// not, and that was worse: the catalog would report a tweak as off because SYSTEM's hive did
/// not have the value, while your own hive had it set the whole time.
///
/// The app itself runs as you, so it can do user-scoped work directly. This routes by scope:
/// machine and process scope over the pipe, user scope in-process. Neither half has to know.
///
/// The real fix for the underlying limitation is for the service to impersonate the console
/// session (<c>WTSQueryUserToken</c> then <c>ImpersonateLoggedOnUser</c>) so it can touch the
/// right hive itself. Until that exists, this puts the work where the correct hive already is.
/// </summary>
public sealed class SplitBackend : IOptimizerBackend
{
    private readonly IOptimizerBackend _service;
    private readonly ILocalBackend _local;

    public SplitBackend(IOptimizerBackend service, ILocalBackend local)
    {
        _service = service;
        _local = local;
    }

    public string Description => _service.Description;

    public bool IsService => true;

    public bool CanApplyMachineScope => _service.CanApplyMachineScope;

    /// <summary>True for tweaks that live in the calling user's hive.</summary>
    private bool IsUserScoped(string tweakId) => _local.ScopeOf(tweakId) == TweakScope.User;

    /// <summary>
    /// True for tweaks this process can carry out itself.
    ///
    /// The user's own hive, and a process in the user's own session. Both are things the app
    /// has the rights for without the service, and a process-scoped tweak in particular is
    /// better done here: the service runs as SYSTEM in session 0, so the pid it is handed comes
    /// from a session it cannot see, and it needs a privilege to reach into one it did not
    /// launch. The app is already sitting in the right session with the right token.
    /// </summary>
    private bool RunsLocally(string tweakId)
        => _local.ScopeOf(tweakId) is TweakScope.User or TweakScope.Process;

    private IOptimizerBackend For(string tweakId) => RunsLocally(tweakId) ? _local : _service;

    public async Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(CancellationToken ct = default)
    {
        var fromService = await _service.GetStatusAsync(ct).ConfigureAwait(false);

        // Only the user-scoped rows are re-read locally. Asking the local engine for everything
        // would report machine-scope tweaks through an unelevated process, which reads fine but
        // would then disagree with what an apply can do.
        var corrected = new List<TweakStatusSummary>(fromService.Count);
        foreach (var status in fromService)
        {
            if (!IsUserScoped(status.Tweak.Id))
            {
                corrected.Add(status);
                continue;
            }

            var local = await _local.GetStatusAsync(status.Tweak.Id, null, ct: ct).ConfigureAwait(false);
            corrected.Add(local ?? status);
        }

        return corrected;
    }

    public Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        TweakTarget? target = null,
        CancellationToken ct = default)
        => For(tweakId).GetStatusAsync(tweakId, options, target, ct);

    public Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        TweakTarget? target = null,
        CancellationToken ct = default)
        => For(tweakId).ApplyAsync(tweakId, options, dryRun, target, ct);

    public Task<IReadOnlyList<ChangeResult>> RevertAsync(string tweakId, CancellationToken ct = default)
        => For(tweakId).RevertAsync(tweakId, ct);

    public async Task<IReadOnlyList<ChangeResult>> RevertAllAsync(CancellationToken ct = default)
    {
        // Both halves, because both may have outstanding changes. They share one journal, so
        // each skips what the other already put back.
        var results = new List<ChangeResult>();
        results.AddRange(await _service.RevertAllAsync(ct).ConfigureAwait(false));
        results.AddRange(await _local.RevertAllAsync(ct).ConfigureAwait(false));

        // The same tweak can be reported by both when the journal changed underneath; keep the
        // first answer for each so the summary line counts every tweak once.
        return [.. results
            .GroupBy(r => r.TweakId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())];
    }

    public Task<IReadOnlyList<JournalLine>> GetJournalAsync(int tail = 60, CancellationToken ct = default)
        => _service.GetJournalAsync(tail, ct);

    public Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(CancellationToken ct = default)
        => _service.GetProfilesAsync(ct);

    /// <summary>
    /// Applies a profile a tweak at a time, so its user-scoped entries are not silently dropped.
    ///
    /// Sending the whole profile to the service would skip every HKCU entry with an
    /// explanation, which for the shipped profiles is most of them.
    /// </summary>
    public async Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(
        string name, Func<BatchProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        var selections = _local.ProfileSelections(name);
        var results = new List<ChangeResult>(selections.Count);

        // This half already worked a tweak at a time, so saying where it has got to costs
        // nothing and is true by construction: the report happens either side of the call that
        // does the work, and a tweak that throws stops the count where it stopped.
        for (var i = 0; i < selections.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var selection = selections[i];

            if (onProgress is not null)
                await onProgress(new BatchProgress(i + 1, selections.Count, selection.TweakId))
                    .ConfigureAwait(false);

            var applied = await For(selection.TweakId)
                .ApplyAsync(selection.TweakId, selection.EffectiveOptions, dryRun: false, ct: ct)
                .ConfigureAwait(false);

            results.AddRange(applied);

            if (onProgress is not null)
            {
                // The row's own outcome, not the batch's. A profile entry is one tweak and comes
                // back as one result; anything else would be a backend doing something
                // unexpected, and reporting it as still running would leave the row spinning.
                var outcome = applied.Count == 1 ? applied[0].Outcome : Outcome.Applied;
                await onProgress(new BatchProgress(i + 1, selections.Count, selection.TweakId, outcome))
                    .ConfigureAwait(false);
            }
        }

        return results;
    }

    /// <summary>
    /// Sends a startup switch to whichever half can actually perform it.
    ///
    /// The same split as the tweaks, decided per entry rather than per call: an HKLM Run value
    /// needs the elevated service, and an HKCU one needs this process, because HKCU inside a
    /// LocalSystem service is SYSTEM's own hive and the write would succeed while changing
    /// nothing the user would ever see.
    /// </summary>
    public Task<StartupSetResult> SetStartupEnabledAsync(
        string id, bool enabled, CancellationToken ct = default)
        => StartupWire.Find(id) is { IsMachineWide: true }
            ? _service.SetStartupEnabledAsync(id, enabled, ct)
            : _local.SetStartupEnabledAsync(id, enabled, ct);

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync().ConfigureAwait(false);
        await _local.DisposeAsync().ConfigureAwait(false);
    }
}
