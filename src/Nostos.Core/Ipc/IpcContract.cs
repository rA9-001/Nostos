// The IPC contract and client used to be their own assembly. It is folded into Core because
// the boundary bought nothing: everything that referenced Nostos.Ipc already referenced
// Nostos.Core, and a 380-line project earns neither a csproj nor a DLL.
//
// The namespace stays Nostos.Ipc. It names a versioned wire protocol rather than a folder, and
// renaming it would touch every file that talks to the service in exchange for nothing. Like
// the rest of Core this code has no Windows dependency, so the protocol is still exercisable in
// tests on any runner; the ACL'd server half lives in the service, which is Windows-only.

namespace Nostos.Ipc;

/// <summary>
/// The wire contract between the unelevated clients and the LocalSystem service.
///
/// Newline-delimited JSON. Chosen over anything generated because the pipe is a privilege
/// boundary into a process that can edit HKLM: the entire protocol has to be readable, and
/// auditable, without tooling.
/// </summary>
public static class IpcContract
{
    /// <summary>
    /// Bumped on any breaking change. The client refuses to talk to a mismatched service.
    ///
    /// v2: removed the process watcher. 'watch-status' became 'safety-status' and lost its
    /// active-session list; PingResult lost AutoProfilesEnabled; ProfileSummary lost its
    /// persistent/session split and trigger list; TweakSummary gained Choices.
    ///
    /// v3: removed the auto-revert watchdog. 'confirm' and 'safety-status' are gone, along with
    /// SafetyStatusResult -- there is no longer any state for a client to ask about, because
    /// nothing reverts a change unless a person asks for it.
    ///
    /// v4: added 'startup-list' and 'startup-set', for the startup manager. Additive, but the
    /// window has to know whether the service it is talking to can answer them, and a client
    /// that silently got an "unknown command" back would show an empty list rather than a
    /// reason. TweakSummary also gained TakesTargetProcess, which is optional on the wire.
    /// </summary>
    public const int ProtocolVersion = 5;

    public const string PipeName = "Nostos.control";

    /// <summary>
    /// Hard cap on a single request. A privileged listener must not let an unprivileged caller
    /// choose how much memory it allocates.
    /// </summary>
    public const int MaxRequestBytes = 256 * 1024;
}

public static class IpcCommands
{
    public const string Ping = "ping";
    public const string List = "list";
    public const string Status = "status";
    public const string Apply = "apply";
    public const string Revert = "revert";
    public const string Journal = "journal";
    public const string Reconcile = "reconcile";
    public const string Profiles = "profiles";
    public const string ApplyProfile = "profile-apply";

    /// <summary>Everything that runs at sign-in, machine-wide and per-user.</summary>
    public const string StartupList = "startup-list";

    /// <summary>Switches one startup entry on or off. Machine-wide entries only; see the daemon.</summary>
    public const string StartupSet = "startup-set";
}
