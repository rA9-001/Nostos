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
    /// </summary>
    public const int ProtocolVersion = 3;

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
}
