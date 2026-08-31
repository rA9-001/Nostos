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

/// <summary>
/// Where a batch has got to, reported once as each tweak starts and once as it finishes.
///
/// This exists so a window can show a profile being applied rather than a spinner over a
/// frozen list. It is deliberately reported from inside the loop that does the work: the
/// alternative -- animating through the list at a plausible rate while the real batch runs
/// somewhere else -- would put something on screen that is not true, and would keep ticking
/// happily past the tweak that failed.
/// </summary>
/// <param name="Index">1-based position in the batch.</param>
/// <param name="Total">How many tweaks the batch will attempt.</param>
/// <param name="TweakId">Which one. The caller already has the catalog, so no title is sent.</param>
/// <param name="Outcome">Null while it is running; set once it is done.</param>
public sealed record BatchProgress(int Index, int Total, string TweakId, Outcome? Outcome = null)
{
    public bool IsRunning => Outcome is null;
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
