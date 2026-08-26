using Nostos.App.Backends;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;

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
        IReadOnlyList<TweakChoice>? choices = null)
        => new(
            new TweakSummary(
                id, title, summary, category,
                TweakScope.Machine, TweakLifetime.Persistent, risk, evidence,
                RequiresReboot: false, RequiresElevation: true, choices ?? []),
            applied, managed, $"{id} state", applicable,
            applicable ? null : "not applicable here");

    public virtual Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TweakStatusSummary>>(Statuses.ToList());

    /// <summary>Records what the view model asked for, so tests can assert on the selections.</summary>
    public List<(string TweakId, IReadOnlyDictionary<string, string>? Options)> StatusReads { get; } = [];

    /// <summary>Applies with the options they carried, for asserting that selections are sent.</summary>
    public List<(string TweakId, IReadOnlyDictionary<string, string>? Options)> Applies { get; } = [];

    /// <summary>Overrides keyed by "tweakId:optionId", so a test can make a selection change the state.</summary>
    public Dictionary<string, TweakStatusSummary> StatusBySelection { get; } = [];

    public virtual Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        CancellationToken ct = default)
    {
        StatusReads.Add((tweakId, options));

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
        CancellationToken ct = default)
    {
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

    public Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(string name, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChangeResult>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
