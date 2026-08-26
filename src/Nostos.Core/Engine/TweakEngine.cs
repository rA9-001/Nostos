using Nostos.Core.Abstractions;
using Nostos.Core.Journal;
using Nostos.Core.Safety;

namespace Nostos.Core.Engine;

/// <summary>
/// Applies and reverts tweaks with the invariant that the machine can always be put back.
///
/// Every apply follows the same order, and the order is the safety property:
/// check -> capture prior value -> journal the intent -> mutate -> verify -> journal the result.
/// The journal write happens before the mutation, so a crash at any point still leaves a
/// durable record of what the value used to be.
/// </summary>
public sealed class TweakEngine
{
    private readonly IJournal _journal;
    private readonly IPrivilegeCheck _privileges;
    private readonly ISafetyGate _gate;
    private readonly ILogSink _log;

    public TweakEngine(
        TweakRegistry registry,
        IJournal journal,
        IPrivilegeCheck? privileges = null,
        ISafetyGate? gate = null,
        ILogSink? log = null)
    {
        Registry = registry;
        _journal = journal;
        _privileges = privileges ?? AlwaysElevated.Instance;
        _gate = gate ?? PermissiveSafetyGate.Instance;
        _log = log ?? NullLogSink.Instance;
    }

    public TweakRegistry Registry { get; }

    // ---------------------------------------------------------------- status

    public async Task<IReadOnlyList<TweakStatus>> GetStatusAsync(
        IEnumerable<ITweak>? subset = null,
        TweakContext? context = null,
        CancellationToken ct = default)
    {
        var ctx = context ?? TweakContext.Default;
        var outstanding = await _journal.GetOutstandingAsync(ct).ConfigureAwait(false);
        var results = new List<TweakStatus>();

        foreach (var tweak in subset ?? Registry.All)
        {
            ct.ThrowIfCancellationRequested();
            Applicability applicability;
            TweakState state;
            try
            {
                applicability = await tweak.CheckApplicabilityAsync(ctx, ct).ConfigureAwait(false);
                state = applicability.IsApplicable
                    ? await tweak.ReadAsync(ctx, ct).ConfigureAwait(false)
                    : TweakState.Unknown(applicability.Reason ?? "not applicable");
            }
            catch (Exception e)
            {
                // Reading must never take down a status listing; an unreadable key is
                // itself information the user wants to see.
                _log.Warn($"{tweak.Metadata.Id}: read failed: {e.Message}");
                applicability = Applicability.Applicable;
                state = TweakState.Unknown($"read failed: {e.Message}");
            }

            results.Add(new TweakStatus(
                tweak.Metadata, state, outstanding.ContainsKey(tweak.Metadata.Id), applicability));
        }

        return results;
    }

    // ----------------------------------------------------------------- apply

    public async Task<TweakOperationResult> ApplyAsync(
        string tweakId, TweakContext? context = null, string origin = "manual", CancellationToken ct = default)
    {
        var results = await ApplyManyAsync([new TweakSelection(tweakId)], context, origin, ct)
            .ConfigureAwait(false);
        return results[0];
    }

    public async Task<IReadOnlyList<TweakOperationResult>> ApplyManyAsync(
        IReadOnlyList<TweakSelection> selections,
        TweakContext? context = null,
        string origin = "manual",
        CancellationToken ct = default)
    {
        var baseContext = context ?? TweakContext.Default;
        var results = new List<TweakOperationResult>(selections.Count);
        var resolved = new List<(ITweak Tweak, TweakContext Context)>();

        foreach (var selection in selections)
        {
            var tweak = Registry.Find(selection.TweakId);
            if (tweak is null)
            {
                results.Add(new TweakOperationResult(
                    selection.TweakId, Outcome.Failed, "unknown tweak id; run `nos list`"));
                continue;
            }
            resolved.Add((tweak, baseContext.With(selection.EffectiveOptions)));
        }

        if (resolved.Count == 0)
            return results;

        var batch = resolved.Select(r => r.Tweak.Metadata).ToList();

        if (FindConflict(batch) is { } conflict)
        {
            foreach (var meta in batch)
                results.Add(new TweakOperationResult(meta.Id, Outcome.Skipped, conflict));
            return results;
        }

        var clearance = await _gate.BeforeBatchAsync(batch, ct).ConfigureAwait(false);
        if (!clearance.Allowed)
        {
            foreach (var meta in batch)
                results.Add(new TweakOperationResult(
                    meta.Id, Outcome.Skipped, clearance.Reason ?? "refused by safety gate"));
            return results;
        }

        foreach (var (tweak, ctx) in resolved)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ApplyOneAsync(tweak, ctx, origin, ct).ConfigureAwait(false));
        }

        // Nothing runs after the batch. A change that landed is the user's to keep or to
        // revert, and the journal entry written before it is what makes reverting possible.
        return results;
    }

    private async Task<TweakOperationResult> ApplyOneAsync(
        ITweak tweak, TweakContext ctx, string origin, CancellationToken ct)
    {
        var meta = tweak.Metadata;

        if (meta.RequiresElevation && !_privileges.IsElevated)
            return new TweakOperationResult(meta.Id, Outcome.Skipped,
                "requires elevation; run the service or start the CLI as administrator");

        if (meta.Scope == TweakScope.Process && ctx.TargetProcessId is null)
            return new TweakOperationResult(meta.Id, Outcome.Skipped,
                "process-scoped tweak needs a target process (--pid or --process)");

        TweakSnapshot snapshot;
        try
        {
            var applicability = await tweak.CheckApplicabilityAsync(ctx, ct).ConfigureAwait(false);
            if (!applicability.IsApplicable)
                return new TweakOperationResult(meta.Id, Outcome.Skipped,
                    applicability.Reason ?? "not applicable to this machine");

            var state = await tweak.ReadAsync(ctx, ct).ConfigureAwait(false);
            if (state.IsApplied && !ctx.GetBool("force", false))
                return new TweakOperationResult(meta.Id, Outcome.AlreadyApplied, state.Description);

            snapshot = await tweak.CaptureAsync(ctx, ct).ConfigureAwait(false);

            if (ctx.DryRun)
                return new TweakOperationResult(meta.Id, Outcome.Skipped,
                    $"dry run: would change {state.Description}", meta.RequiresReboot);
        }
        catch (Exception e)
        {
            _log.Error($"{meta.Id}: pre-apply check failed", e);
            return new TweakOperationResult(meta.Id, Outcome.Failed, e.Message, false, e);
        }

        // Durable before destructive. Everything after this point is recoverable.
        await _journal.AppendAsync(
            JournalEntry.Create(meta.Id, JournalAction.ApplyIntent, ctx, origin, snapshot), ct)
            .ConfigureAwait(false);

        try
        {
            await tweak.ApplyAsync(ctx, ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log.Error($"{meta.Id}: apply failed, rolling back", e);
            await _journal.AppendAsync(
                JournalEntry.Create(meta.Id, JournalAction.ApplyFailed, ctx, origin, error: e.Message), ct)
                .ConfigureAwait(false);

            try
            {
                await tweak.RevertAsync(snapshot, ctx, CancellationToken.None).ConfigureAwait(false);
                await _journal.AppendAsync(
                    JournalEntry.Create(meta.Id, JournalAction.RevertCommitted, ctx, origin,
                        detail: "automatic rollback after failed apply"), CancellationToken.None)
                    .ConfigureAwait(false);
                return new TweakOperationResult(meta.Id, Outcome.RolledBack,
                    $"apply failed and was rolled back: {e.Message}", false, e);
            }
            catch (Exception rollbackError)
            {
                // Worst case. Say so plainly rather than pretending the machine is clean.
                _log.Error($"{meta.Id}: ROLLBACK FAILED", rollbackError);
                await _journal.AppendAsync(
                    JournalEntry.Create(meta.Id, JournalAction.RevertFailed, ctx, origin,
                        error: rollbackError.Message), CancellationToken.None)
                    .ConfigureAwait(false);
                return new TweakOperationResult(meta.Id, Outcome.Failed,
                    $"apply failed AND rollback failed: {rollbackError.Message}. " +
                    $"The prior value is recorded in {AppPaths.JournalPath}.", false, rollbackError);
            }
        }

        bool? verified = null;
        try
        {
            verified = await tweak.VerifyAsync(ctx, ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log.Warn($"{meta.Id}: verify threw: {e.Message}");
        }

        var newState = await SafeReadAsync(tweak, ctx, ct).ConfigureAwait(false);
        await _journal.AppendAsync(
            JournalEntry.Create(meta.Id, JournalAction.ApplyCommitted, ctx, origin,
                verified: verified, detail: newState), ct)
            .ConfigureAwait(false);

        return verified is false
            ? new TweakOperationResult(meta.Id, Outcome.Unverified,
                "applied, but the value did not read back as expected", meta.RequiresReboot)
            : new TweakOperationResult(meta.Id, Outcome.Applied, newState, meta.RequiresReboot);
    }

    // ---------------------------------------------------------------- revert

    public async Task<TweakOperationResult> RevertAsync(
        string tweakId, TweakContext? context = null, string origin = "manual", CancellationToken ct = default)
    {
        var outstanding = await _journal.GetOutstandingAsync(ct).ConfigureAwait(false);
        if (!outstanding.TryGetValue(tweakId, out var snapshot))
            return new TweakOperationResult(tweakId, Outcome.NothingToRevert,
                "no un-reverted change recorded for this tweak");

        return await RevertOneAsync(tweakId, snapshot, context ?? TweakContext.Default, origin, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Undoes everything this program has done, newest first. This is the promise the whole
    /// design exists to keep, so it must work even when the catalog has changed underneath
    /// the journal.
    /// </summary>
    public async Task<IReadOnlyList<TweakOperationResult>> RevertAllAsync(
        TweakContext? context = null, string origin = "manual", CancellationToken ct = default)
    {
        var outstanding = await _journal.GetOutstandingAsync(ct).ConfigureAwait(false);
        var ctx = context ?? TweakContext.Default;
        var results = new List<TweakOperationResult>();

        // Reverse order: later tweaks may depend on state established by earlier ones.
        foreach (var (tweakId, snapshot) in outstanding.Reverse())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RevertOneAsync(tweakId, snapshot, ctx, origin, ct).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<TweakOperationResult> RevertOneAsync(
        string tweakId, TweakSnapshot snapshot, TweakContext ctx, string origin, CancellationToken ct)
    {
        var tweak = Registry.Find(tweakId);
        if (tweak is null)
        {
            // The journal outlives the catalog: a tweak removed in a later release still has
            // an outstanding snapshot on someone's machine.
            return new TweakOperationResult(tweakId, Outcome.Failed,
                $"tweak no longer exists in this build; its prior value is preserved in {AppPaths.JournalPath}");
        }

        if (ctx.DryRun)
            return new TweakOperationResult(tweakId, Outcome.Skipped,
                $"dry run: would restore the value captured {snapshot.CapturedUtc:u}");

        if (tweak.Metadata.RequiresElevation && !_privileges.IsElevated)
            return new TweakOperationResult(tweakId, Outcome.Skipped, "requires elevation");

        try
        {
            await tweak.RevertAsync(snapshot, ctx, ct).ConfigureAwait(false);
            await _journal.AppendAsync(
                JournalEntry.Create(tweakId, JournalAction.RevertCommitted, ctx, origin,
                    detail: await SafeReadAsync(tweak, ctx, ct).ConfigureAwait(false)), ct)
                .ConfigureAwait(false);
            return new TweakOperationResult(tweakId, Outcome.Reverted,
                $"restored value captured {snapshot.CapturedUtc:u}", tweak.Metadata.RequiresReboot);
        }
        catch (Exception e)
        {
            _log.Error($"{tweakId}: revert failed", e);
            await _journal.AppendAsync(
                JournalEntry.Create(tweakId, JournalAction.RevertFailed, ctx, origin, error: e.Message), ct)
                .ConfigureAwait(false);
            return new TweakOperationResult(tweakId, Outcome.Failed, e.Message, false, e);
        }
    }

    // ----------------------------------------------------------------- misc

    /// <summary>
    /// Re-applies tweaks the journal says should be on but the machine says are off.
    /// Windows Update resets several of these keys; without this the user's settings quietly
    /// rot and the user blames the game.
    /// </summary>
    public async Task<IReadOnlyList<TweakOperationResult>> ReconcileAsync(
        TweakContext? context = null, CancellationToken ct = default)
    {
        var outstanding = await _journal.GetOutstandingAsync(ct).ConfigureAwait(false);
        var ctx = context ?? TweakContext.Default;
        var drifted = new List<TweakSelection>();

        foreach (var tweakId in outstanding.Keys)
        {
            var tweak = Registry.Find(tweakId);
            if (tweak is null || tweak.Metadata.Lifetime != TweakLifetime.Persistent)
                continue;
            try
            {
                if (!(await tweak.ReadAsync(ctx, ct).ConfigureAwait(false)).IsApplied)
                {
                    _log.Warn($"{tweakId}: drifted back to an unmanaged value, re-applying");
                    drifted.Add(new TweakSelection(tweakId));
                }
            }
            catch (Exception e)
            {
                _log.Warn($"{tweakId}: drift check failed: {e.Message}");
            }
        }

        return drifted.Count == 0
            ? []
            : await ApplyManyAsync(drifted, ctx, "reconcile", ct).ConfigureAwait(false);
    }

    private static string? FindConflict(IReadOnlyList<TweakMetadata> batch)
    {
        var ids = batch.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in batch)
        {
            foreach (var other in meta.ConflictsWith)
            {
                if (ids.Contains(other))
                    return $"'{meta.Id}' conflicts with '{other}'; the batch was not applied";
            }
        }
        return null;
    }

    private static async Task<string> SafeReadAsync(ITweak tweak, TweakContext ctx, CancellationToken ct)
    {
        try
        {
            return (await tweak.ReadAsync(ctx, ct).ConfigureAwait(false)).Description;
        }
        catch (Exception e)
        {
            return $"(read-back failed: {e.Message})";
        }
    }
}
