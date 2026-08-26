using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nostos.Win32.Interop;

namespace Nostos.Win32.Services;

/// <summary>Quality-of-service state of a process, as far as EcoQoS is concerned.</summary>
public enum QosMode
{
    /// <summary>No explicit opinion: the scheduler decides, which on Win11 can mean throttling.</summary>
    SystemManaged,

    /// <summary>Explicitly throttled. What we set on background apps while a game is running.</summary>
    Efficiency,

    /// <summary>Explicitly not throttled. What we set on the game.</summary>
    HighPerformance,
}

/// <summary>
/// Live, per-process tuning. Everything here is undone by the process exiting, which makes it
/// the safest category in the catalog and the one that can be changed mid-match.
///
/// All of it operates on the process from the outside via documented APIs. Nothing reads or
/// writes another process's memory.
/// </summary>
public static class ProcessControl
{
    public static ProcessPriorityClass GetPriority(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.PriorityClass;
    }

    public static void SetPriority(int pid, ProcessPriorityClass priority)
    {
        using var process = Process.GetProcessById(pid);
        process.PriorityClass = priority;
    }

    public static nint GetAffinity(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.ProcessorAffinity;
    }

    public static void SetAffinity(int pid, nint mask)
    {
        if (mask == 0)
            throw new ArgumentException("An affinity mask of 0 would leave the process no core to run on.", nameof(mask));

        using var process = Process.GetProcessById(pid);
        process.ProcessorAffinity = mask;
    }

    public static QosMode GetQos(int pid)
    {
        var handle = OpenOrThrow(pid, NativeMethods.ProcessAccess.QueryLimitedInformation);
        try
        {
            var state = new NativeMethods.ProcessPowerThrottlingState
            {
                Version = NativeMethods.ProcessPowerThrottlingCurrentVersion,
            };
            var size = (uint)Marshal.SizeOf<NativeMethods.ProcessPowerThrottlingState>();

            if (!NativeMethods.GetProcessInformation(
                    handle, NativeMethods.ProcessInformationClass.ProcessPowerThrottling, ref state, size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessInformation failed.");

            if ((state.ControlMask & NativeMethods.ProcessPowerThrottlingExecutionSpeed) == 0)
                return QosMode.SystemManaged;

            return (state.StateMask & NativeMethods.ProcessPowerThrottlingExecutionSpeed) != 0
                ? QosMode.Efficiency
                : QosMode.HighPerformance;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Sets EcoQoS participation.
    ///
    /// <see cref="QosMode.SystemManaged"/> clears our opinion entirely (ControlMask = 0) rather
    /// than guessing a default, so reverting genuinely restores the pre-tweak behaviour.
    /// </summary>
    public static void SetQos(int pid, QosMode mode)
    {
        var handle = OpenOrThrow(pid,
            NativeMethods.ProcessAccess.SetInformation | NativeMethods.ProcessAccess.QueryLimitedInformation);
        try
        {
            var state = new NativeMethods.ProcessPowerThrottlingState
            {
                Version = NativeMethods.ProcessPowerThrottlingCurrentVersion,
                ControlMask = mode == QosMode.SystemManaged ? 0 : NativeMethods.ProcessPowerThrottlingExecutionSpeed,
                StateMask = mode == QosMode.Efficiency ? NativeMethods.ProcessPowerThrottlingExecutionSpeed : 0,
            };
            var size = (uint)Marshal.SizeOf<NativeMethods.ProcessPowerThrottlingState>();

            if (!NativeMethods.SetProcessInformation(
                    handle, NativeMethods.ProcessInformationClass.ProcessPowerThrottling, ref state, size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetProcessInformation failed.");
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Finds running processes by image name, with or without the ".exe".</summary>
    public static IReadOnlyList<Process> FindByName(string imageName)
    {
        var name = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? imageName[..^4]
            : imageName;
        return Process.GetProcessesByName(name);
    }

    private static IntPtr OpenOrThrow(int pid, NativeMethods.ProcessAccess access)
    {
        var handle = NativeMethods.OpenProcess(access, false, (uint)pid);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenProcess({pid}) failed.");
        return handle;
    }
}
