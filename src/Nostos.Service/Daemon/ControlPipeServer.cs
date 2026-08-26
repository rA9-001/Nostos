using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Nostos.Core.Abstractions;
using Nostos.Ipc;
using Nostos.Win32.ServiceControl;

namespace Nostos.Service.Daemon;

/// <summary>
/// The control pipe: the only way in to a LocalSystem process that can rewrite HKLM.
///
/// Security properties, in order of importance:
/// <list type="number">
/// <item>The DACL is explicit. SYSTEM and Administrators, plus the SIDs recorded at install
/// time. Never Everyone, never Authenticated Users — a world-writable control pipe into this
/// process would be a local privilege escalation, and that is the bug most tools in this
/// category actually ship.</item>
/// <item>Requests are size-capped, so an unprivileged caller cannot choose how much memory the
/// privileged process allocates.</item>
/// <item>A handler that throws returns an error to that one caller and closes that one
/// connection. It never takes down the listener.</item>
/// </list>
/// </summary>
public sealed class ControlPipeServer
{
    private const int MaxConcurrentConnections = 4;

    private readonly ServiceConfiguration _configuration;
    private readonly ILogSink _log;
    private readonly Func<IpcRequest, CancellationToken, Task<IpcResponse>> _handler;

    public ControlPipeServer(
        ServiceConfiguration configuration,
        ILogSink log,
        Func<IpcRequest, CancellationToken, Task<IpcResponse>> handler)
    {
        _configuration = configuration;
        _log = log;
        _handler = handler;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listeners = Enumerable.Range(0, MaxConcurrentConnections)
            .Select(i => ListenLoopAsync(i, ct))
            .ToArray();

        await Task.WhenAll(listeners).ConfigureAwait(false);
    }

    private async Task ListenLoopAsync(int index, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await ServeAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                _log.Warn($"pipe listener {index}: {e.Message}");

                // Back off briefly so a persistent failure (bad SID in config, name collision)
                // does not become a hot loop that fills the log.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                if (pipe is not null)
                    await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // The account this process runs under needs FullControl, which is the only right that
        // includes CreateNewInstance. Without it only the FIRST listener can be created and
        // every additional instance fails with access-denied -- silently capping the service at
        // one concurrent client. Under the installed service this is SYSTEM (already granted);
        // it matters when the daemon runs in the foreground under a user account.
        using (var self = WindowsIdentity.GetCurrent())
        {
            if (self.User is { } owner)
            {
                security.AddAccessRule(new PipeAccessRule(
                    owner, PipeAccessRights.FullControl, AccessControlType.Allow));
            }
        }

        foreach (var sid in _configuration.AllowedSids)
        {
            try
            {
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(sid),
                    // Enough to send requests and read replies. Not enough to change the pipe's
                    // own security descriptor.
                    PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                    AccessControlType.Allow));
            }
            catch (ArgumentException)
            {
                _log.Warn($"ignoring malformed SID in service.json: '{sid}'");
            }
        }

        return NamedPipeServerStreamAcl.Create(
            IpcContract.PipeName,
            PipeDirection.InOut,
            MaxConcurrentConnections,
            PipeTransmissionMode.Byte,
            // Not CurrentUserOnly: the service runs as SYSTEM, so that flag would lock out the
            // very user the pipe exists for. The explicit DACL above is the access control.
            PipeOptions.Asynchronous,
            inBufferSize: 8192,
            outBufferSize: 8192,
            security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var caller = DescribeCaller(pipe);
        _log.Debug($"pipe: connection from {caller}");

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = false,
        };

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await ReadCappedLineAsync(reader, ct).ConfigureAwait(false);
            if (line is null)
                break;

            IpcResponse response;
            var requestId = "unknown";
            try
            {
                var request = JsonSerializer.Deserialize(line, IpcJsonContext.Default.IpcRequest)
                    ?? throw new InvalidDataException("empty request");
                requestId = request.Id;

                _log.Debug($"pipe: {caller} -> {request.Command}");
                response = await _handler(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                // The caller gets the message; the service keeps running.
                _log.Warn($"pipe: request failed: {e.Message}");
                response = IpcResponse.Failure(requestId, e.Message);
            }

            await writer.WriteLineAsync(
                JsonSerializer.Serialize(response, IpcJsonContext.Default.IpcResponse).AsMemory(),
                ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }

        _log.Debug($"pipe: {caller} disconnected");
    }

    /// <summary>Reads one line, refusing anything over the cap instead of buffering it.</summary>
    private static async Task<string?> ReadCappedLineAsync(StreamReader reader, CancellationToken ct)
    {
        var builder = new StringBuilder();
        var buffer = new char[1024];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                return builder.Length > 0 ? builder.ToString() : null;

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                    return builder.ToString().TrimEnd('\r');
                builder.Append(buffer[i]);
            }

            if (builder.Length > IpcContract.MaxRequestBytes)
                throw new InvalidDataException(
                    $"request exceeded {IpcContract.MaxRequestBytes} bytes and was refused");
        }
    }

    private static string DescribeCaller(NamedPipeServerStream pipe)
    {
        try
        {
            // Impersonating purely to read the caller's identity for the log; the service does
            // no work under the caller's token.
            var name = "unknown";
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                name = identity.Name;
            });
            return name;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
