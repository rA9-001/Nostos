using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Nostos.Ipc;

/// <summary>Raised when the service is not installed, not running, or refuses the caller.</summary>
public sealed class ServiceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Client half of the control pipe.
///
/// One connection carries many requests: the UI holds it open so applying a tweak mid-match is
/// a single write, not a connect-handshake-write-close cycle.
/// </summary>
public sealed class OptimizerClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _turn = new(1, 1);

    private OptimizerClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = false };
    }

    public static async Task<OptimizerClient> ConnectAsync(
        int timeoutMilliseconds = 3000, CancellationToken ct = default)
    {
        var pipe = new NamedPipeClientStream(
            ".", IpcContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMilliseconds, ct).ConfigureAwait(false);
        }
        catch (TimeoutException e)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new ServiceUnavailableException(
                "The optimizer service is not running. Install it with " +
                "`Nostos.Service.exe install` from an elevated prompt, or drop --service " +
                "to apply changes directly.", e);
        }
        catch (UnauthorizedAccessException e)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new ServiceUnavailableException(
                "The optimizer service refused this account. The control pipe only accepts the " +
                "account that installed the service, plus administrators.", e);
        }

        return new OptimizerClient(pipe);
    }

    /// <summary>Round-trips one request. Serialised, so concurrent callers queue rather than interleave.</summary>
    public async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(
                JsonSerializer.Serialize(request, IpcJsonContext.Default.IpcRequest).AsMemory(),
                ct).ConfigureAwait(false);
            await _writer.FlushAsync(ct).ConfigureAwait(false);

            var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                ?? throw new ServiceUnavailableException("The service closed the connection.");

            return JsonSerializer.Deserialize(line, IpcJsonContext.Default.IpcResponse)
                ?? throw new ServiceUnavailableException("The service returned an unreadable response.");
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task<T> CallAsync<T>(string command, object? payload, CancellationToken ct)
        where T : class
    {
        var response = await SendAsync(IpcRequest.Create(command, payload), ct).ConfigureAwait(false);
        if (!response.Ok)
            throw new InvalidOperationException(response.Error ?? $"'{command}' failed.");

        return response.ResultAs<T>()
            ?? throw new InvalidOperationException($"'{command}' returned no result.");
    }

    public async Task<PingResult> PingAsync(CancellationToken ct = default)
    {
        var result = await CallAsync<PingResult>(IpcCommands.Ping, null, ct).ConfigureAwait(false);

        if (result.ProtocolVersion != IpcContract.ProtocolVersion)
        {
            throw new ServiceUnavailableException(
                $"Protocol mismatch: this client speaks v{IpcContract.ProtocolVersion}, the installed " +
                $"service speaks v{result.ProtocolVersion}. Reinstall the service to match.");
        }

        return result;
    }

    public Task<List<TweakSummary>> ListAsync(CancellationToken ct = default)
        => CallAsync<List<TweakSummary>>(IpcCommands.List, null, ct);

    public Task<List<TweakStatusSummary>> StatusAsync(ChangeRequest request, CancellationToken ct = default)
        => CallAsync<List<TweakStatusSummary>>(IpcCommands.Status, request, ct);

    public Task<List<ChangeResult>> ApplyAsync(ChangeRequest request, CancellationToken ct = default)
        => CallAsync<List<ChangeResult>>(IpcCommands.Apply, request, ct);

    public Task<List<ChangeResult>> RevertAsync(ChangeRequest request, CancellationToken ct = default)
        => CallAsync<List<ChangeResult>>(IpcCommands.Revert, request, ct);

    public Task<List<ChangeResult>> ReconcileAsync(CancellationToken ct = default)
        => CallAsync<List<ChangeResult>>(IpcCommands.Reconcile, null, ct);

    public Task<List<JournalLine>> JournalAsync(int tail = 30, CancellationToken ct = default)
        => CallAsync<List<JournalLine>>(IpcCommands.Journal, new JournalRequest(tail), ct);

    public Task<List<ProfileSummary>> ProfilesAsync(CancellationToken ct = default)
        => CallAsync<List<ProfileSummary>>(IpcCommands.Profiles, null, ct);

    public Task<List<ChangeResult>> ApplyProfileAsync(
        ApplyProfileRequest request, CancellationToken ct = default)
        => CallAsync<List<ChangeResult>>(IpcCommands.ApplyProfile, request, ct);

    public async ValueTask DisposeAsync()
    {
        _writer.Dispose();
        _reader.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
        _turn.Dispose();
    }
}
