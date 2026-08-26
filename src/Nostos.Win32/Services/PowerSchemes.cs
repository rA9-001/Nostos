using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Nostos.Win32.Interop;

namespace Nostos.Win32.Services;

/// <summary>Reads, activates and unhides Windows power schemes through powrprof, not powercfg.exe.</summary>
public static class PowerSchemes
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorFileNotFound = 2;

    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    /// <summary>
    /// Ships hidden on desktop SKUs since 1803. It is High Performance with core parking and
    /// latency-tolerance idling turned off; on battery-powered machines it is a straight
    /// battery-life loss, which is why the tweak that uses it refuses to run on one.
    /// </summary>
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static Guid GetActive()
    {
        var rc = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (rc != ErrorSuccess)
            throw new Win32Exception((int)rc, "PowerGetActiveScheme failed.");
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    public static void SetActive(Guid scheme)
    {
        var rc = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, scheme);
        if (rc != ErrorSuccess)
            throw new Win32Exception((int)rc, $"PowerSetActiveScheme({scheme}) failed.");
    }

    public static bool Exists(Guid scheme) => TryGetFriendlyName(scheme, out _);

    public static string GetFriendlyName(Guid scheme)
        => TryGetFriendlyName(scheme, out var name) ? name : scheme.ToString();

    private static bool TryGetFriendlyName(Guid scheme, out string name)
    {
        name = "";
        uint size = 0;
        var rc = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (rc == ErrorFileNotFound || size == 0)
            return false;
        if (rc != ErrorSuccess)
            return false;

        var buffer = new byte[size];
        rc = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
        if (rc != ErrorSuccess)
            return false;

        name = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return true;
    }

    /// <summary>
    /// Removes a scheme. Used to clean up a scheme we unhid, so reverting leaves no extra entry
    /// in the user's power menu. Fails if the scheme is currently active, so activate another first.
    /// </summary>
    public static void Delete(Guid scheme)
    {
        var rc = NativeMethods.PowerDeleteScheme(IntPtr.Zero, scheme);
        if (rc != ErrorSuccess && rc != ErrorFileNotFound)
            throw new Win32Exception((int)rc, $"PowerDeleteScheme({scheme}) failed.");
    }

    /// <summary>
    /// Makes a hidden scheme selectable, equivalent to `powercfg -duplicatescheme`.
    /// Returns the GUID that actually ended up on the machine, which is not always the
    /// template GUID, so callers must journal the returned value rather than assume.
    /// </summary>
    public static Guid EnsureAvailable(Guid template)
    {
        if (Exists(template))
            return template;

        var destination = IntPtr.Zero;
        var rc = NativeMethods.PowerDuplicateScheme(IntPtr.Zero, template, ref destination);
        if (rc != ErrorSuccess)
            throw new Win32Exception((int)rc, $"PowerDuplicateScheme({template}) failed.");

        try
        {
            return Marshal.PtrToStructure<Guid>(destination);
        }
        finally
        {
            NativeMethods.LocalFree(destination);
        }
    }
}
