using Nostos.Win32.Apps;
using Nostos.Win32.Cleanup;
using Nostos.Core.Localization;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.App.Backends;

/// <summary>
/// Talks to the LocalSystem service over the control pipe.
///
/// The preferred backend: the privileged work happens in a process that is already privileged,
/// so nothing the user does in the UI raises a UAC prompt — including changing a tweak while a
/// game is running.
/// </summary>
public sealed class ServiceBackend : IOptimizerBackend
{
    private readonly OptimizerClient _client;

    private ServiceBackend(OptimizerClient client, PingResult ping)
    {
        _client = client;
        _serviceVersion = ping.ServiceVersion;
    }

    private readonly string _serviceVersion;

    public string Description => Strings.Format("connection.service", _serviceVersion);

    public bool IsService => true;

    // The service runs as LocalSystem, so machine scope is always available through it.
    public bool CanApplyMachineScope => true;

    public static async Task<ServiceBackend> ConnectAsync(CancellationToken ct = default)
    {
        var client = await OptimizerClient.ConnectAsync(ct: ct).ConfigureAwait(false);
        try
        {
            var ping = await client.PingAsync(ct).ConfigureAwait(false);
            return new ServiceBackend(client, ping);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(CancellationToken ct = default)
        => await _client.StatusAsync(new ChangeRequest(), ct).ConfigureAwait(false);

    public async Task<TweakStatusSummary?> GetStatusAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options,
        TweakTarget? target = null,
        CancellationToken ct = default)
    {
        var statuses = await _client.StatusAsync(new ChangeRequest
        {
            TweakIds = [tweakId],
            Options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            TargetProcessId = target?.ProcessId,
            TargetProcessName = target?.ProcessName,
        }, ct).ConfigureAwait(false);

        return statuses.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        TweakTarget? target = null,
        CancellationToken ct = default)
        => await _client.ApplyAsync(new ChangeRequest
        {
            TweakIds = [tweakId],
            Options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DryRun = dryRun,
            TargetProcessId = target?.ProcessId,
            TargetProcessName = target?.ProcessName,
            Origin = "gui",
        }, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ChangeResult>> RevertAsync(string tweakId, CancellationToken ct = default)
        => await _client.RevertAsync(
            new ChangeRequest { TweakIds = [tweakId], Origin = "gui" }, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ChangeResult>> RevertAllAsync(CancellationToken ct = default)
        => await _client.RevertAsync(
            new ChangeRequest { All = true, Origin = "gui" }, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<JournalLine>> GetJournalAsync(int tail = 60, CancellationToken ct = default)
        => await _client.JournalAsync(tail, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(CancellationToken ct = default)
        => await _client.ProfilesAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Not over the pipe. Removing a program means running somebody else's uninstaller, and the
    /// one process that must never do that is the LocalSystem service.
    ///
    /// In practice this is not reached: the app wraps this backend in <see cref="SplitBackend"/>,
    /// which sends removals to the local half. It refuses rather than silently doing nothing, so
    /// a future caller finds out here rather than from a row that never changes.
    /// </summary>
    public Task<RemovalReport> RemoveAppAsync(
        string appId, Func<RemovalProgress, Task>? onProgress = null, CancellationToken ct = default)
        => Task.FromResult(new RemovalReport(
            appId, appId, false, false, 0, 0, 0, [],
            ["the service does not run uninstallers; this has to be done by the app"]));

    /// <summary>Not over the pipe either, for the same reason: the app owns removals.</summary>
    public Task<RemovalReport> ForgetAppAsync(
        string appId, Func<RemovalProgress, Task>? onProgress = null, CancellationToken ct = default)
        => Task.FromResult(new RemovalReport(
            appId, appId, false, false, 0, 0, 0, [],
            ["the service does not remove registrations; this has to be done by the app"]));

    /// <summary>Nor this one.</summary>
    public Task<RemovalReport> RemoveItemAsync(
        string appId, string path, Func<RemovalProgress, Task>? onProgress = null,
        CancellationToken ct = default)
        => Task.FromResult(new RemovalReport(
            appId, appId, false, false, 0, 0, 0, [],
            ["the service does not remove app files or keys; this has to be done by the app"]));

    /// <summary>
    /// Also not over the pipe. Every cleanup target is either inside a user's own profile --
    /// which a LocalSystem service is the wrong process to be walking -- or needs an elevated
    /// launch of the app, so there is nothing here the service could do that the app cannot.
    /// </summary>
    public Task<IReadOnlyList<CleanupFinding>> ScanCleanupAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CleanupFinding>>([]);

    public Task<CleanupResult> CleanAsync(
        string targetId, Func<string, Task>? onProgress = null, CancellationToken ct = default)
        => Task.FromResult(new CleanupResult(
            targetId, 0, 0, 0, ["the service does not empty caches; this has to be done by the app"]));

    /// <summary>
    /// The service applies the whole profile in one call, so there is nothing to report as it
    /// goes: the pipe is one request and one response, and adding progress frames to it is a
    /// change to a privilege boundary that a progress bar does not justify.
    ///
    /// <paramref name="onProgress"/> is therefore ignored here and the window shows an
    /// indeterminate bar. In practice this is not the path the window takes -- the app wraps
    /// this in <see cref="SplitBackend"/>, which applies a profile a tweak at a time and does
    /// report -- but a backend that silently reported nothing while its caller believed it
    /// would is worse than one that says so out loud here.
    /// </summary>
    public async Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(
        string name, Func<BatchProgress, Task>? onProgress = null, CancellationToken ct = default)
        => await _client.ApplyProfileAsync(new ApplyProfileRequest(name), ct).ConfigureAwait(false);

    public async Task<StartupSetResult> SetStartupEnabledAsync(
        string id, bool enabled, CancellationToken ct = default)
        => await _client.StartupSetAsync(new StartupSetRequest(id, enabled), ct).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
