using Nostos.Ipc;

namespace Nostos.App.Backends;

public static class BackendFactory
{
    /// <summary>
    /// Prefers the service, falls back to driving the engine in-process.
    ///
    /// The fallback is not an error path: the app is fully usable without the service, just
    /// without automatic profiles and without machine-scope changes from an unelevated session.
    /// The reason is returned so the UI can explain it rather than leaving controls mysteriously
    /// inert.
    /// </summary>
    public static async Task<(IOptimizerBackend Backend, string? FallbackReason)> CreateAsync(
        CancellationToken ct = default)
    {
        try
        {
            var service = await ServiceBackend.ConnectAsync(ct).ConfigureAwait(false);

            // Wrapped, not used directly: user-scoped tweaks have to run in this process, where
            // the correct user hive is. See SplitBackend.
            return (new SplitBackend(service, new LocalBackend()), null);
        }
        catch (ServiceUnavailableException e)
        {
            return (new LocalBackend(), e.Message);
        }
        catch (Exception e)
        {
            // Anything unexpected from the pipe is still not a reason to refuse to start.
            return (new LocalBackend(), $"could not reach the service: {e.Message}");
        }
    }
}
