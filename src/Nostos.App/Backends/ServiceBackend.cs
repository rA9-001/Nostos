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
        Description = $"service v{ping.ServiceVersion}";
    }

    public string Description { get; }

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
        CancellationToken ct = default)
    {
        var statuses = await _client.StatusAsync(new ChangeRequest
        {
            TweakIds = [tweakId],
            Options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        }, ct).ConfigureAwait(false);

        return statuses.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        string tweakId,
        IReadOnlyDictionary<string, string>? options = null,
        bool dryRun = false,
        CancellationToken ct = default)
        => await _client.ApplyAsync(new ChangeRequest
        {
            TweakIds = [tweakId],
            Options = options ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DryRun = dryRun,
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

    public async Task<IReadOnlyList<ChangeResult>> ApplyProfileAsync(string name, CancellationToken ct = default)
        => await _client.ApplyProfileAsync(new ApplyProfileRequest(name), ct).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
