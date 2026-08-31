using Nostos.Ipc;

namespace Nostos.Win32.Services;

/// <summary>
/// Converts startup entries between the Windows layer and the wire.
///
/// It lives here rather than in Core because the mapping needs <see cref="StartupSource"/>,
/// which is a Windows concept; Core stays free of any Windows dependency, which is what lets
/// the protocol be exercised in tests on a runner that has no registry.
///
/// Both halves of the split need this, and both are Windows-only: the service answers
/// <c>startup-list</c> from here, and the app enumerates locally from the same code rather than
/// asking over a pipe for something it can read itself.
/// </summary>
public static class StartupWire
{
    public static StartupEntry ToWire(this StartupItem item) => new(
        item.Id,
        item.Name,
        item.Source.ToString(),
        item.Command,
        item.ExecutablePath,
        item.IsEnabled,
        item.Location,
        item.IsMachineWide);

    public static List<StartupEntry> ToWire(this IEnumerable<StartupItem> items)
        => [.. items.Select(ToWire)];

    /// <summary>
    /// Finds the live entry an id refers to, re-reading the machine rather than trusting the id.
    ///
    /// This is the whole security story for <c>startup-set</c>. The service is asked to switch
    /// "the entry called user-run:Steam", and it goes and finds that entry itself; it is never
    /// handed a registry path and a payload by an unprivileged caller. An id naming nothing is
    /// simply not found, which is also what happens when something was uninstalled between the
    /// list being drawn and a switch being clicked.
    /// </summary>
    public static StartupItem? Find(string id)
        => StartupItems.List().FirstOrDefault(
            i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
}
