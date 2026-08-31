namespace Nostos.Core.Abstractions;

/// <summary>
/// One reversible change to the machine.
///
/// The five-method shape is the whole architecture: nothing may be applied that cannot be
/// read, captured, reverted and verified. A tweak that cannot implement <see cref="CaptureAsync"/>
/// honestly does not belong in the catalog.
/// </summary>
public interface ITweak
{
    TweakMetadata Metadata { get; }

    /// <summary>Reads the live value. Must never mutate anything, and must work unelevated where possible.</summary>
    Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default);

    /// <summary>Captures the prior value so <see cref="RevertAsync"/> can restore exactly it.</summary>
    Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default);

    Task ApplyAsync(TweakContext context, CancellationToken ct = default);

    /// <summary>Restores the captured prior value. Must be idempotent and must tolerate a stale snapshot.</summary>
    Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default);

    /// <summary>
    /// Confirms the change actually stuck. Run right after Apply, and again periodically —
    /// Windows Update silently reverts several of these keys.
    /// </summary>
    Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default);

    /// <summary>
    /// Whether this tweak is applicable to the current machine at all (right OS build, right hardware).
    /// Returning a reason string keeps the UI honest instead of showing a control that silently no-ops.
    /// </summary>
    Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(Applicability.Applicable);
}

/// <param name="IsApplicable">False when the tweak cannot work here.</param>
/// <param name="Reason">
/// Shown to the user when not applicable, e.g. "needs Windows 11 22H2 or later". Always
/// English: this is what the CLI prints, what goes in a log, and what a bug report quotes.
/// </param>
/// <param name="ReasonKey">
/// The string-table key for <paramref name="Reason"/>, when one exists.
///
/// Carried alongside the English rather than instead of it, because these are produced deep in
/// the tweak layer -- which, on an installed copy, is running inside a machine-wide service
/// under SYSTEM with no user and no language. The service cannot know what language to answer
/// in, so it sends both and lets whoever is displaying it decide. A window with a language
/// setting looks the key up; the CLI, the journal and the log ignore it.
/// </param>
/// <param name="ReasonArgs">
/// What to substitute into the translated string, in order. Strings rather than objects so the
/// pair survives the trip over the pipe as ordinary JSON.
/// </param>
public readonly record struct Applicability(
    bool IsApplicable,
    string? Reason = null,
    string? ReasonKey = null,
    IReadOnlyList<string>? ReasonArgs = null)
{
    public static readonly Applicability Applicable = new(true);

    /// <summary>Not applicable, with English text only. Nothing translates this one.</summary>
    public static Applicability No(string reason) => new(false, reason);

    /// <summary>Not applicable, with English text and the key that translates it.</summary>
    public static Applicability No(string reasonKey, string reason, params string[] args)
        => new(false, reason, reasonKey, args);
}
