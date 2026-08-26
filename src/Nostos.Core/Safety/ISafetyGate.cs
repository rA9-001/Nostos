using Nostos.Core.Abstractions;

namespace Nostos.Core.Safety;

/// <param name="Allowed">False stops the apply before anything is touched.</param>
/// <param name="Reason">Shown to the user when refused.</param>
public readonly record struct SafetyClearance(bool Allowed, string? Reason = null)
{
    public static readonly SafetyClearance Allow = new(true);
    public static SafetyClearance Refuse(string reason) => new(false, reason);
}

/// <summary>
/// Runs before a batch of changes and can refuse it.
///
/// The Windows implementation takes a System Restore point; the default implementation allows
/// everything, so tests and dry runs stay cheap.
///
/// There is deliberately no "after" hook. There used to be, and its only job was arming an
/// auto-revert timer that undid unconfirmed changes on its own. Nothing undoes a change on this
/// machine's behalf any more: what this program applies stays applied until a person reverts it.
/// An empty extension point invites the behaviour back, so it is gone rather than unused.
/// </summary>
public interface ISafetyGate
{
    Task<SafetyClearance> BeforeBatchAsync(IReadOnlyList<TweakMetadata> batch, CancellationToken ct = default);
}

public sealed class PermissiveSafetyGate : ISafetyGate
{
    public static readonly PermissiveSafetyGate Instance = new();

    public Task<SafetyClearance> BeforeBatchAsync(IReadOnlyList<TweakMetadata> batch, CancellationToken ct = default)
        => Task.FromResult(SafetyClearance.Allow);
}
