using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.App.Backends;

/// <summary>
/// What the UI needs, whichever half is doing the work.
///
/// The window never knows whether it is talking to the privileged service or driving the
/// engine in-process. That is what lets the app be useful before the service is installed,
/// and lets it stop asking for elevation once it is.
///
/// The transport DTOs from <c>Nostos.Ipc</c> are reused as the UI's own model rather
/// than mapped into a third set of types: one shape, one place to change it.
/// </summary>
public interface IOptimizerBackend : IAsyncDisposable
{
    /// <summary>Short label for the status bar, e.g. "service v0.1.0" or "direct, not elevated".</summary>
    string Description { get; }

    bool IsService { get; }

    /// <summary>False when machine-scope tweaks will be skipped, so the UI can say why up front.</summary>
    bool CanApplyMachineScope { get; }

    Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-reads one tweak under a specific set of choices.
    ///
    /// Needed because "is this applied" depends on which option is selected, and the bulk
    /// status call has nowhere to put per-tweak selections.
    /// </summary>
    Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        TweakTarget? target = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        TweakTarget? target = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChangeResult>> RevertAsync(string tweakId, CancellationToken ct = default);

    Task<IReadOnlyList<ChangeResult>> RevertAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<JournalLine>> GetJournalAsync(int tail = 60, CancellationToken ct = default);

    Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(CancellationToken ct = default);

    /// <param name="onProgress">
    /// Called as each tweak starts and again as it finishes, so the window can show the profile
    /// being worked through. Optional, and genuinely optional: a backend that cannot report
    /// real progress passes nothing, and the window shows an indeterminate bar rather than an
    /// animation it made up.
    /// </param>
    Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(
        string name,
        Func<BatchProgress, Task>? onProgress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Switches one startup entry on or off.
    ///
    /// Only the write goes through the backend. Reading the list needs no privilege, so the
    /// window enumerates it in-process rather than over the pipe -- which also means the startup
    /// tab works before the service is installed, and shows per-user entries even when the
    /// machine-wide half is unavailable.
    /// </summary>
    Task<StartupSetResult> SetStartupEnabledAsync(
        string id, bool enabled, CancellationToken ct = default);
}

/// <summary>
/// The process a process-scoped tweak acts on.
///
/// Carried alongside the options rather than inside them: the options dictionary is a tweak's
/// own choices, it goes into the journal as the record of what was asked for, and a process id
/// is neither a choice nor worth keeping -- it is meaningless the moment the process exits.
///
/// The name travels with the id purely so the journal and the log can say "notepad.exe (12345)"
/// rather than a bare number nobody can identify afterwards.
/// </summary>
public sealed record TweakTarget(int ProcessId, string? ProcessName);

/// <summary>
/// A backend that also has the catalog and the profiles to hand, because it is running in this
/// process rather than answering over a pipe.
///
/// <see cref="SplitBackend"/> needs both to decide where each tweak should go, and taking them
/// through an interface keeps it testable without a real registry underneath.
/// </summary>
public interface ILocalBackend : IOptimizerBackend
{
    /// <summary>Scope of a tweak, or null when this catalog does not have it.</summary>
    TweakScope? ScopeOf(string tweakId);

    /// <summary>The selections in a named profile, in order.</summary>
    IReadOnlyList<TweakSelection> ProfileSelections(string name);
}
