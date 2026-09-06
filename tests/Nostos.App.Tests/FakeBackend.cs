using Nostos.App.Backends;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;
using Nostos.Win32.Apps;
using Nostos.Win32.Cleanup;

namespace Nostos.App.Tests;

/// <summary>An in-memory backend, so the view model can be driven without a machine or a service.</summary>
public class FakeBackend : IOptimizerBackend
{
    public List<TweakStatusSummary> Statuses { get; } = [];
    public List<ProfileSummary> ProfileList { get; } = [];
    public List<JournalLine> JournalLines { get; } = [];

    public List<string> Applied { get; } = [];
    public List<string> Reverted { get; } = [];
    public int RevertAllCount { get; private set; }

    public string Description { get; init; } = "fake";
    public bool IsService { get; init; }
    public bool CanApplyMachineScope { get; init; } = true;

    public static TweakStatusSummary Tweak(
        string id,
        string category = TweakCategories.Performance,
        Risk risk = Risk.Safe,
        Evidence evidence = Evidence.Plausible,
        bool applied = false,
        bool managed = false,
        bool applicable = true,
        string title = "Title",
        string summary = "Summary",
        IReadOnlyList<TweakChoice>? choices = null,
        IReadOnlyList<string>? tags = null,
        bool requiresElevation = true)
        => new(
            new TweakSummary(
                id, title, summary, category,
                TweakScope.Machine, TweakLifetime.Persistent, risk, evidence,
                RequiresReboot: false, RequiresElevation: requiresElevation, choices ?? [],
                TakesTargetProcess: null, Tags: tags),
            applied, managed, $"{id} state", applicable,
            applicable ? null : "not applicable here");

    /// <summary>Every startup switch asked for, so a test can assert what the window sent.</summary>
    public List<(string Id, bool Enabled)> StartupSets { get; } = [];

    /// <summary>Set to make the next switch come back refused, the way an unelevated one does.</summary>
    public string? StartupRefusal { get; set; }

    public virtual Task<StartupSetResult> SetStartupEnabledAsync(
        string id, bool enabled, CancellationToken ct = default)
    {
        StartupSets.Add((id, enabled));

        return Task.FromResult(StartupRefusal is { } refusal
            ? new StartupSetResult(id, false, refusal)
            : new StartupSetResult(id, true, enabled ? "enabled" : "disabled"));
    }

    public virtual Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TweakStatusSummary>>(Statuses.ToList());

    /// <summary>Records what the view model asked for, so tests can assert on the selections.</summary>
    public List<(string TweakId, IReadOnlyDictionary<string, string>? Options)> StatusReads { get; } = [];

    /// <summary>Applies with the options they carried, for asserting that selections are sent.</summary>
    public List<(string TweakId, IReadOnlyDictionary<string, string>? Options)> Applies { get; } = [];

    /// <summary>Overrides keyed by "tweakId:optionId", so a test can make a selection change the state.</summary>
    public Dictionary<string, TweakStatusSummary> StatusBySelection { get; } = [];

    /// <summary>Every target sent with a read or an apply, so a test can assert what was aimed at.</summary>
    public List<(string TweakId, TweakTarget? Target)> Targets { get; } = [];

    public virtual Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        TweakTarget? target = null,
        CancellationToken ct = default)
    {
        StatusReads.Add((tweakId, options));
        Targets.Add((tweakId, target));

        foreach (var (key, value) in options ?? new Dictionary<string, string>())
        {
            if (StatusBySelection.TryGetValue($"{tweakId}:{value}", out var overridden))
                return Task.FromResult<TweakStatusSummary?>(overridden);

            _ = key;
        }

        return Task.FromResult(Statuses.FirstOrDefault(s =>
            string.Equals(s.Tweak.Id, tweakId, StringComparison.OrdinalIgnoreCase)));
    }

    public virtual Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        TweakTarget? target = null,
        CancellationToken ct = default)
    {
        Targets.Add((tweakId, target));

        if (!dryRun)
        {
            Applied.Add(tweakId);
            Applies.Add((tweakId, options));
        }

        return Task.FromResult<IReadOnlyList<ChangeResult>>(
            [new ChangeResult(tweakId, dryRun ? Outcome.Skipped : Outcome.Applied, "ok", false)]);
    }

    public Task<IReadOnlyList<ChangeResult>> RevertAsync(string tweakId, CancellationToken ct = default)
    {
        Reverted.Add(tweakId);
        return Task.FromResult<IReadOnlyList<ChangeResult>>(
            [new ChangeResult(tweakId, Outcome.Reverted, "ok", false)]);
    }

    public Task<IReadOnlyList<ChangeResult>> RevertAllAsync(CancellationToken ct = default)
    {
        RevertAllCount++;
        return Task.FromResult<IReadOnlyList<ChangeResult>>([]);
    }

    public Task<IReadOnlyList<JournalLine>> GetJournalAsync(int tail = 60, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JournalLine>>(JournalLines.ToList());

    public Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProfileSummary>>(ProfileList.ToList());

    /// <summary>What a profile apply should report and return, keyed by profile name.</summary>
    public Dictionary<string, IReadOnlyList<ChangeResult>> ProfileResults { get; } = [];

    /// <summary>Every profile applied, so a test can assert which card was acted on.</summary>
    public List<string> ProfilesApplied { get; } = [];

    /// <summary>
    /// Runs between the "starting" and "finished" report for each tweak.
    ///
    /// A test's only chance to look at the card mid-run: the apply is one await, so without a
    /// hook here the rows would be back to their finished state by the time it returned.
    /// </summary>
    public Func<BatchProgress, Task>? WhileRunning { get; set; }

    public async Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(
        string name, Func<BatchProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        ProfilesApplied.Add(name);

        if (!ProfileResults.TryGetValue(name, out var results))
            return [];

        for (var i = 0; i < results.Count; i++)
        {
            var starting = new BatchProgress(i + 1, results.Count, results[i].TweakId);

            if (onProgress is not null)
                await onProgress(starting);

            if (WhileRunning is not null)
                await WhileRunning(starting);

            if (onProgress is not null)
                await onProgress(new BatchProgress(
                    i + 1, results.Count, results[i].TweakId, results[i].Outcome));
        }

        return results;
    }

    /// <summary>Removal reports to hand back, keyed by app id.</summary>
    public Dictionary<string, RemovalReport> RemovalResults { get; } = [];

    /// <summary>Every app removal asked for, so a test can assert which row was acted on.</summary>
    public List<string> AppsRemoved { get; } = [];

    /// <summary>The stages to report, so a test can watch a row move through them.</summary>
    public List<RemovalStage> RemovalStages { get; } =
        [RemovalStage.Uninstalling, RemovalStage.Scanning, RemovalStage.Sweeping, RemovalStage.Finished];

    public async Task<RemovalReport> RemoveAppAsync(
        string appId, Func<RemovalProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        AppsRemoved.Add(appId);

        foreach (var stage in RemovalStages)
        {
            if (onProgress is not null)
                await onProgress(new RemovalProgress(stage, appId));
        }

        return RemovalResults.TryGetValue(appId, out var report)
            ? report
            : new RemovalReport(appId, appId, true, false, 0, 0, 0, [], []);
    }

    /// <summary>Ids the window asked to forget, kept apart from the ones it asked to remove.</summary>
    public List<string> AppsForgotten { get; } = [];

    public async Task<RemovalReport> ForgetAppAsync(
        string appId, Func<RemovalProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        AppsForgotten.Add(appId);

        foreach (var stage in RemovalStages.Where(s => s != RemovalStage.Uninstalling))
        {
            if (onProgress is not null)
                await onProgress(new RemovalProgress(stage, appId));
        }

        return RemovalResults.TryGetValue(appId, out var report)
            ? report
            : new RemovalReport(appId, appId, false, false, 0, 1, 0, [], []);
    }

    /// <summary>Each (id, path) the window asked to remove on its own.</summary>
    public List<(string Id, string Path)> ItemsRemoved { get; } = [];

    /// <summary>What to answer for a given path, so a test can make one item fail.</summary>
    public Dictionary<string, RemovalReport> ItemResults { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RemovalReport> RemoveItemAsync(
        string appId, string path, Func<RemovalProgress, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        ItemsRemoved.Add((appId, path));

        if (onProgress is not null)
        {
            await onProgress(new RemovalProgress(RemovalStage.Sweeping, path));
            await onProgress(new RemovalProgress(RemovalStage.Finished, appId));
        }

        return ItemResults.TryGetValue(path, out var report)
            ? report
            : new RemovalReport(appId, appId, false, false, 1, 0, 0, [], []);
    }

    /// <summary>What the scan should answer. Empty unless a test sets it.</summary>
    public List<CleanupFinding> CleanupFindings { get; } = [];

    /// <summary>Target ids the window asked to empty, in order.</summary>
    public List<string> Cleaned { get; } = [];

    public Task<IReadOnlyList<CleanupFinding>> ScanCleanupAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CleanupFinding>>(CleanupFindings);

    public Task<CleanupResult> CleanAsync(
        string targetId, Func<string, Task>? onProgress = null, CancellationToken ct = default)
    {
        Cleaned.Add(targetId);

        return Task.FromResult(CleanupResults.TryGetValue(targetId, out var result)
            ? result
            : new CleanupResult(targetId, 1024 * 1024, 3, 0, []));
    }

    /// <summary>What to answer for a given target, so a test can make one fail.</summary>
    public Dictionary<string, CleanupResult> CleanupResults { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
