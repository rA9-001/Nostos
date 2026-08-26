using Nostos.Core.Abstractions;
using Nostos.Ipc;

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

    private IOptimizerBackend For(string tweakId) => IsUserScoped(tweakId) ? _local : _service;

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

            var local = await _local.GetStatusAsync(status.Tweak.Id, null, ct).ConfigureAwait(false);
            corrected.Add(local ?? status);
        }

        return corrected;
    }

    public Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken ct = default)
        => For(tweakId).GetStatusAsync(tweakId, options, ct);

    public Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        CancellationToken ct = default)
        => For(tweakId).ApplyAsync(tweakId, options, dryRun, ct);

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
        string name, CancellationToken ct = default)
    {
        var selections = _local.ProfileSelections(name);
        var results = new List<ChangeResult>(selections.Count);

        foreach (var selection in selections)
        {
            ct.ThrowIfCancellationRequested();

            results.AddRange(await For(selection.TweakId)
                .ApplyAsync(selection.TweakId, selection.EffectiveOptions, dryRun: false, ct)
                .ConfigureAwait(false));
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync().ConfigureAwait(false);
        await _local.DisposeAsync().ConfigureAwait(false);
    }
}
