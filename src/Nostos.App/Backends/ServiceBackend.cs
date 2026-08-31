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
