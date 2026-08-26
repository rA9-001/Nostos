using System.Runtime.InteropServices;

namespace Nostos.Win32.ServiceControl;

/// <summary>
/// The Service Control Manager surface, hand-written.
///
/// Deliberately not <c>Microsoft.Extensions.Hosting.WindowsServices</c>: that package drags the
/// whole generic-host and DI stack into a process whose entire job is to own a pipe and a
/// timer. Keeping the product free of NuGet packages means a reviewer can read everything that
/// runs as LocalSystem, and it keeps the shipped binary small enough not to look like the
/// packed droppers that AV heuristics are trained on.
///
/// Lives in the shared Win32 library rather than in the service executable so the desktop app
/// can inspect and install the service itself, instead of the user being told to go and run a
/// second program from an elevated prompt.
/// </summary>
internal static class ServiceInterop
{
    // --------------------------------------------------------------- constants

    internal const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;

    internal const uint SERVICE_STOPPED = 0x00000001;
    internal const uint SERVICE_START_PENDING = 0x00000002;
    internal const uint SERVICE_STOP_PENDING = 0x00000003;
    internal const uint SERVICE_RUNNING = 0x00000004;

    internal const uint SERVICE_ACCEPT_STOP = 0x00000001;
    internal const uint SERVICE_ACCEPT_SHUTDOWN = 0x00000004;

    internal const uint SERVICE_CONTROL_STOP = 0x00000001;
    internal const uint SERVICE_CONTROL_INTERROGATE = 0x00000004;
    internal const uint SERVICE_CONTROL_SHUTDOWN = 0x00000005;

    internal const uint SC_MANAGER_ALL_ACCESS = 0x000F003F;

    /// <summary>Enough to open the SCM and query. Available to unelevated callers.</summary>
    internal const uint SC_MANAGER_CONNECT = 0x0001;

    internal const uint SERVICE_QUERY_STATUS = 0x0004;

    /// <summary>Enough to read the registered command line. Available to unelevated callers.</summary>
    internal const uint SERVICE_QUERY_CONFIG = 0x0001;

    /// <summary>Leaves a ChangeServiceConfig field as it is.</summary>
    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    internal const uint SERVICE_ALL_ACCESS = 0x000F01FF;

    internal const uint SERVICE_BOOT_START = 0x00000000;
    internal const uint SERVICE_SYSTEM_START = 0x00000001;
    internal const uint SERVICE_AUTO_START = 0x00000002;
    internal const uint SERVICE_DEMAND_START = 0x00000003;
    internal const uint SERVICE_DISABLED = 0x00000004;

    internal const uint SERVICE_ERROR_NORMAL = 0x00000001;

    /// <summary>Rewrite the configuration. Requires elevation.</summary>
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;

    internal const uint SERVICE_STOP = 0x0020;

    internal const uint SERVICE_ENUMERATE_DEPENDENTS = 0x0008;

    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_DEPENDENT_SERVICES_RUNNING = 1051;

    internal const uint SERVICE_CONFIG_DESCRIPTION = 1;
    internal const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    internal const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    internal const uint SC_ACTION_RESTART = 1;

    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    internal const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    internal const int ERROR_SERVICE_NOT_ACTIVE = 1062;

    // --------------------------------------------------------------- structures

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    /// <summary>
    /// QUERY_SERVICE_CONFIGW. The string fields are pointers into the caller-supplied buffer,
    /// not marshalled strings, so the buffer has to outlive the read of each one.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ServiceConfigInfo
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ServiceName;

        public IntPtr ServiceProc;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ServiceDescription
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceDelayedAutoStartInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DelayedAutostart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScAction
    {
        public uint Type;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ServiceFailureActions
    {
        public uint ResetPeriod;
        public IntPtr RebootMessage;
        public IntPtr Command;
        public uint ActionCount;
        public IntPtr Actions;
    }

    // --------------------------------------------------------------- callbacks

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal delegate void ServiceMainCallback(int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint ServiceControlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context);

    // ------------------------------------------------------------ service side

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "StartServiceCtrlDispatcherW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StartServiceCtrlDispatcher(ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "RegisterServiceCtrlHandlerExW")]
    internal static extern IntPtr RegisterServiceCtrlHandlerEx(
        string serviceName, ServiceControlHandler handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);

    // ---------------------------------------------------------- installer side

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "OpenSCManagerW")]
    internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "CreateServiceW")]
    internal static extern IntPtr CreateService(
        IntPtr scManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPath,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")]
    internal static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartServiceW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StartService(IntPtr service, uint argc, string[]? argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ControlService(IntPtr service, uint control, ref ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatus(IntPtr service, ref ServiceStatus status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "QueryServiceConfigW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceConfig(
        IntPtr service, IntPtr config, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "ChangeServiceConfigW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPath,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "ChangeServiceConfig2W")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, IntPtr info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);
}
