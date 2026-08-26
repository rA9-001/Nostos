using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;

namespace Nostos.Core.Tests;

/// <summary>
/// An in-memory tweak with a settable "machine value".
///
/// Lets the engine's ordering guarantees — capture before mutate, rollback on failure,
/// revert-to-captured — be tested exhaustively without touching a real machine.
/// </summary>
public sealed class FakeTweak : ITweak
{
    private readonly string _desired;

    public FakeTweak(
        string id = "test.tweak",
        string initialValue = "original",
        string desiredValue = "tweaked",
        TweakScope scope = TweakScope.Machine,
        TweakLifetime lifetime = TweakLifetime.Persistent,
        Risk risk = Risk.Safe)
    {
        MachineValue = initialValue;
        _desired = desiredValue;
        Metadata = new TweakMetadata
        {
            Id = id,
            Title = id,
            Summary = "test fixture",
            Category = TweakCategories.Performance,
            Scope = scope,
            Lifetime = lifetime,
            Risk = risk,
            Evidence = Evidence.Measured,
            RequiresElevation = false,
        };
    }

    public TweakMetadata Metadata { get; init; }

    /// <summary>Stands in for the registry value, power scheme, or process property.</summary>
    public string MachineValue { get; set; }

    public bool ThrowOnApply { get; set; }
    public bool ThrowOnRevert { get; set; }
    public bool VerifyResult { get; set; } = true;
    public Applicability Applicability { get; set; } = Applicability.Applicable;

    public int ApplyCount { get; private set; }
    public int RevertCount { get; private set; }

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(Applicability);

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(new TweakState(MachineValue == _desired, $"value = {MachineValue}"));

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject { ["prior"] = MachineValue }));

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        ApplyCount++;
        if (ThrowOnApply)
        {
            // Mutate first, then throw: a half-applied change is exactly the case rollback exists for.
            MachineValue = "half-applied";
            throw new InvalidOperationException("apply blew up");
        }

        MachineValue = _desired;
        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        RevertCount++;
        if (ThrowOnRevert)
            throw new InvalidOperationException("revert blew up");

        MachineValue = snapshot.Data["prior"]?.GetValue<string>() ?? MachineValue;
        return Task.CompletedTask;
    }

    public Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(VerifyResult && MachineValue == _desired);
}
