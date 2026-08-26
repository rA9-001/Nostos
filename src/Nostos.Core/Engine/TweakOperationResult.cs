namespace Nostos.Core.Engine;

public enum Outcome
{
    Applied,
    AlreadyApplied,
    Reverted,
    NothingToRevert,

    /// <summary>Blocked before anything changed: not applicable, not elevated, conflicting, or refused by the safety gate.</summary>
    Skipped,

    /// <summary>Apply threw and the captured snapshot was successfully restored.</summary>
    RolledBack,

    /// <summary>Apply reported success but Verify could not confirm it. The change is journaled and revertible.</summary>
    Unverified,

    Failed,
}

public sealed record TweakOperationResult(
    string TweakId,
    Outcome Outcome,
    string Message,
    bool RequiresReboot = false,
    Exception? Error = null)
{
    /// <summary>
    /// True for everything that is not an error. A skip is a deliberate, explained decision
    /// (dry run, not applicable, needs elevation) and must not be reported as a failure.
    /// </summary>
    public bool IsSuccess => Outcome is not (Outcome.Failed or Outcome.RolledBack);

    /// <summary>True when the machine actually changed.</summary>
    public bool ChangedSomething => Outcome is Outcome.Applied or Outcome.Reverted or Outcome.Unverified;

    public override string ToString() => $"{Outcome,-15} {TweakId,-40} {Message}";
}
