using Nostos.Win32.Removal;

namespace Nostos.App.Startup;

/// <summary>
/// Taking the app off the machine, behind an interface.
///
/// The seam exists for one reason: the only honest test of a removal view model is one that
/// drives it through a declined prompt, a partial failure and a clean run, and none of those
/// can be arranged against a real Service Control Manager without a machine to sacrifice.
/// </summary>
public interface ISystemRemover
{
    /// <summary>What is on this machine. Read on demand, because it queries the SCM.</summary>
    RemovalTargets Inspect();

    Task<RemovalResult> RemoveAsync(CancellationToken ct = default);
}

/// <summary>The real one. See <see cref="SystemRemoval"/>.</summary>
public sealed class WindowsRemover : ISystemRemover
{
    // Resolved the same way the service setup resolves it, which means a development tree finds
    // the sibling build output rather than concluding this copy ships no helper.
    private static string? Helper => Bootstrapper.FindServiceExecutable();

    public RemovalTargets Inspect() => SystemRemoval.Inspect(Helper);

    public Task<RemovalResult> RemoveAsync(CancellationToken ct = default)
        => SystemRemoval.RunAsync(Helper, ct);
}
