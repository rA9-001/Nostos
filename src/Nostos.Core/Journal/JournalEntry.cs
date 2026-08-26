using Nostos.Core.Abstractions;

namespace Nostos.Core.Journal;

/// <summary>
/// Lifecycle of a change, written in order.
///
/// The intent record goes to disk <em>before</em> the machine is touched. That ordering is the
/// whole point: if the process is killed, the machine bluescreens, or the power goes out
/// mid-apply, the prior value is already durable and `nos revert --all` can still undo it.
/// </summary>
public enum JournalAction
{
    /// <summary>Prior value captured, change about to be made.</summary>
    ApplyIntent,

    /// <summary>Change made successfully.</summary>
    ApplyCommitted,

    /// <summary>Change failed. The tweak stays outstanding: a partial apply is still a change.</summary>
    ApplyFailed,

    RevertCommitted,

    RevertFailed,
}

/// <summary>
/// One line of the append-only change log. The journal is the single source of truth for
/// "what has this program done to this machine", and the only input `nos revert --all` needs.
/// </summary>
public sealed record JournalEntry
{
    public required string EntryId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string TweakId { get; init; }
    public required JournalAction Action { get; init; }

    /// <summary>Prior value. Carried on <see cref="JournalAction.ApplyIntent"/>; null elsewhere.</summary>
    public TweakSnapshot? Snapshot { get; init; }

    public IReadOnlyDictionary<string, string> Options
    {
        get;
        init => field = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int? TargetProcessId { get; init; }

    /// <summary>Free-form origin tag: "manual", "profile:competitive", "auto:cyberpunk2077.exe".</summary>
    public string Origin { get; init => field = value ?? "manual"; } = "manual";

    /// <summary>True when Verify confirmed the change landed. Null when Verify was not run.</summary>
    public bool? Verified { get; init; }

    public string? Error { get; init; }

    /// <summary>Human-readable note, e.g. "SystemResponsiveness 20 -> 10".</summary>
    public string? Detail { get; init; }

    public static JournalEntry Create(
        string tweakId,
        JournalAction action,
        TweakContext ctx,
        string origin,
        TweakSnapshot? snapshot = null,
        bool? verified = null,
        string? error = null,
        string? detail = null)
        => new()
        {
            EntryId = Guid.NewGuid().ToString("n"),
            TimestampUtc = DateTimeOffset.UtcNow,
            TweakId = tweakId,
            Action = action,
            Snapshot = snapshot,
            Options = ctx.Options,
            TargetProcessId = ctx.TargetProcessId,
            Origin = origin,
            Verified = verified,
            Error = error,
            Detail = detail,
        };
}
