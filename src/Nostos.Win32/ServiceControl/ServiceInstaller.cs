using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nostos.Win32.ServiceControl;

public enum ServiceState
{
    NotInstalled,
    Stopped,
    Starting,
    Stopping,
    Running,
    Unknown,
}

/// <summary>
/// Install, uninstall, start, stop and query the optimizer service.
///
/// Queries use read-only access so an unelevated caller — the desktop app on every launch —
/// can find out what state the service is in without prompting for anything. Only the
/// mutating operations need administrator rights.
/// </summary>
public static class ServiceInstaller
{
    public const string ServiceName = "Nostos";

    // Spelled out rather than left as the bare product name: this is what somebody sees in
    // services.msc months later while working out what a service called "Nostos" is for.
    public const string DisplayName = "Nostos Gaming Optimizer";

    // Says what it actually does. An earlier version of this string claimed the service
    // "activates profiles when games start", which was true of a process watcher that was
    // deliberately removed -- see "Nothing runs while you play" in docs/architecture.md. A
    // service description that overstates what a LocalSystem process does is exactly the kind
    // of thing that should not be left to rot.
    private const string Description =
        "Applies and reverts gaming performance tweaks on request, and re-applies the ones " +
        "Windows has reset behind your back. Does not watch running processes, and never undoes " +
        "a change on its own. Every change is journaled and revertible.";

    /// <summary>
    /// Registers the service.
    /// </summary>
    /// <param name="binaryPath">
    /// Full path to the service executable. Passed in rather than inferred, because the
    /// installer now runs from whichever process is doing the setup, not only from the service
    /// executable itself.
    /// </param>
    /// <param name="allowedSid">Account permitted on the control pipe. Defaults to the caller.</param>
    public static void Install(string binaryPath, string? allowedSid = null)
    {
        if (!File.Exists(binaryPath))
            throw new FileNotFoundException($"Service executable not found: {binaryPath}", binaryPath);

        var manager = OpenManager();
        try
        {
            // The trailing "run" argument is what tells the launched process it is a service
            // rather than a console invocation.
            var commandLine = $"\"{binaryPath}\" run";

            var service = ServiceInterop.OpenService(
                manager, ServiceName, ServiceInterop.SERVICE_ALL_ACCESS);

            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ServiceInterop.ERROR_SERVICE_DOES_NOT_EXIST)
                    throw new Win32Exception(error, "OpenService failed.");

                service = ServiceInterop.CreateService(
                    manager,
                    ServiceName,
                    DisplayName,
                    ServiceInterop.SERVICE_ALL_ACCESS,
                    ServiceInterop.SERVICE_WIN32_OWN_PROCESS,
                    ServiceInterop.SERVICE_AUTO_START,
                    ServiceInterop.SERVICE_ERROR_NORMAL,
                    commandLine,
                    loadOrderGroup: null,
                    tagId: IntPtr.Zero,
                    dependencies: null,
                    // null account means LocalSystem, which is what machine-scope tweaks need.
                    serviceStartName: null,
                    password: null);

                if (service == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateService failed.");
            }
            // Already registered, but possibly pointing somewhere else: re-point it at this copy
            // rather than failing. A folder that has been moved, or a copy installed from a build
            // tree that has since been cleaned, otherwise leaves behind a service the SCM can no
            // longer launch and that nothing in the app is able to repair.
            else if (!ServiceInterop.ChangeServiceConfig(
                         service,
                         ServiceInterop.SERVICE_NO_CHANGE,
                         ServiceInterop.SERVICE_AUTO_START,
                         ServiceInterop.SERVICE_NO_CHANGE,
                         commandLine,
                         loadOrderGroup: null,
                         tagId: IntPtr.Zero,
                         dependencies: null,
                         serviceStartName: null,
                         password: null,
                         displayName: null))
            {
                ServiceInterop.CloseServiceHandle(service);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig failed.");
            }

            try
            {
                SetDescription(service);
                SetDelayedAutoStart(service);
                SetRestartOnFailure(service);
            }
            finally
            {
                ServiceInterop.CloseServiceHandle(service);
            }

            // Record who may drive the service. Without this the pipe accepts only SYSTEM and
            // administrators, and the unelevated UI could not connect to the thing it just set up.
            var existing = ServiceConfiguration.Load();
            var sid = allowedSid ?? ServiceConfiguration.CurrentUserSid();
            (existing with
            {
                AllowedSids = existing.AllowedSids.Contains(sid)
                    ? existing.AllowedSids
                    : [.. existing.AllowedSids, sid],
            }).Save();
        }
        finally
        {
            ServiceInterop.CloseServiceHandle(manager);
        }
    }

    public static void Uninstall()
    {
        var manager = OpenManager();
        try
        {
            var service = ServiceInterop.OpenService(
                manager, ServiceName, ServiceInterop.SERVICE_ALL_ACCESS);

            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ServiceInterop.ERROR_SERVICE_DOES_NOT_EXIST)
                    return;
                throw new Win32Exception(error, "OpenService failed.");
            }

            try
            {
                TryStop(service);
                if (!ServiceInterop.DeleteService(service))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DeleteService failed.");
            }
            finally
            {
                ServiceInterop.CloseServiceHandle(service);
            }
        }
        finally
        {
            ServiceInterop.CloseServiceHandle(manager);
        }
    }

    public static void Start() => WithService(service =>
    {
        if (ServiceInterop.StartService(service, 0, null))
        {
            WaitForState(service, ServiceInterop.SERVICE_RUNNING);
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ServiceInterop.ERROR_SERVICE_ALREADY_RUNNING)
            throw new Win32Exception(error, "StartService failed.");
    });

    public static void Stop() => WithService(TryStop);

    public static bool IsInstalled() => QueryState() != ServiceState.NotInstalled;

    /// <summary>Reads the current state. Safe to call unelevated, and never throws.</summary>
    public static ServiceState QueryState()
    {
        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
            return ServiceState.Unknown;

        try
        {
            var service = ServiceInterop.OpenService(
                manager, ServiceName, ServiceInterop.SERVICE_QUERY_STATUS);

            if (service == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error() == ServiceInterop.ERROR_SERVICE_DOES_NOT_EXIST
                    ? ServiceState.NotInstalled
                    : ServiceState.Unknown;
            }

            try
            {
                var status = new ServiceInterop.ServiceStatus();
                if (!ServiceInterop.QueryServiceStatus(service, ref status))
                    return ServiceState.Unknown;

                return status.CurrentState switch
                {
                    ServiceInterop.SERVICE_STOPPED => ServiceState.Stopped,
                    ServiceInterop.SERVICE_START_PENDING => ServiceState.Starting,
                    ServiceInterop.SERVICE_STOP_PENDING => ServiceState.Stopping,
                    ServiceInterop.SERVICE_RUNNING => ServiceState.Running,
                    _ => ServiceState.Unknown,
                };
            }
            finally
            {
                ServiceInterop.CloseServiceHandle(service);
            }
        }
        finally
        {
            ServiceInterop.CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// The executable the SCM would launch, or null when the service is not installed or the
    /// command line cannot be read. Safe to call unelevated, and never throws.
    ///
    /// The app uses this to notice that a registered service points at a copy that no longer
    /// exists — the state you land in after moving or deleting the folder you first ran from.
    /// </summary>
    public static string? QueryBinaryPath()
    {
        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
            return null;

        try
        {
            var service = ServiceInterop.OpenService(
                manager, ServiceName, ServiceInterop.SERVICE_QUERY_CONFIG);

            if (service == IntPtr.Zero)
                return null;

            try
            {
                // Ask for the size first: the structure is a header followed by the strings it
                // points into, so a fixed buffer would be a guess.
                ServiceInterop.QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
                if (needed == 0)
                    return null;

                var buffer = Marshal.AllocHGlobal((int)needed);
                try
                {
                    if (!ServiceInterop.QueryServiceConfig(service, buffer, needed, out _))
                        return null;

                    var config = Marshal.PtrToStructure<ServiceInterop.ServiceConfigInfo>(buffer);
                    return ExecutableFromCommandLine(Marshal.PtrToStringUni(config.BinaryPathName));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                ServiceInterop.CloseServiceHandle(service);
            }
        }
        finally
        {
            ServiceInterop.CloseServiceHandle(manager);
        }
    }

    /// <summary>Strips the quotes and the trailing "run" from a registered command line.</summary>
    internal static string? ExecutableFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        commandLine = commandLine.Trim();

        if (commandLine[0] == '"')
        {
            var closing = commandLine.IndexOf('"', 1);
            return closing > 1 ? commandLine[1..closing] : null;
        }

        // Unquoted paths are ambiguous once they contain spaces, and Windows resolves them by
        // probing. We only ever register a quoted path, so treat this as a foreign registration
        // and take the conservative reading: everything up to the first space.
        var space = commandLine.IndexOf(' ');
        return space < 0 ? commandLine : commandLine[..space];
    }

    private static void TryStop(IntPtr service)
    {
        var status = new ServiceInterop.ServiceStatus();
        if (ServiceInterop.ControlService(service, ServiceInterop.SERVICE_CONTROL_STOP, ref status))
        {
            WaitForState(service, ServiceInterop.SERVICE_STOPPED);
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ServiceInterop.ERROR_SERVICE_NOT_ACTIVE)
            throw new Win32Exception(error, "ControlService(STOP) failed.");
    }

    private static void WaitForState(IntPtr service, uint desired)
    {
        // The daemon may be finishing a revert; give it the same window the SCM was told about.
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            var status = new ServiceInterop.ServiceStatus();
            if (!ServiceInterop.QueryServiceStatus(service, ref status) || status.CurrentState == desired)
                return;
            Thread.Sleep(200);
        }
    }

    private static void SetDescription(IntPtr service)
    {
        var description = new ServiceInterop.ServiceDescription { Description = Description };
        WithStruct(description, pointer =>
        {
            if (!ServiceInterop.ChangeServiceConfig2(
                    service, ServiceInterop.SERVICE_CONFIG_DESCRIPTION, pointer))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting the description failed.");
        });
    }

    private static void SetDelayedAutoStart(IntPtr service)
    {
        // Delayed start keeps the service out of the boot critical path. Nothing here is needed
        // in the first seconds of a session: the reconcile pass tolerates arriving late, and the
        // pipe only matters once somebody opens the app.
        var info = new ServiceInterop.ServiceDelayedAutoStartInfo { DelayedAutostart = true };
        WithStruct(info, pointer =>
        {
            if (!ServiceInterop.ChangeServiceConfig2(
                    service, ServiceInterop.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, pointer))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting delayed autostart failed.");
        });
    }

    private static void SetRestartOnFailure(IntPtr service)
    {
        // A service that dies stops being the thing that lets an unelevated app change machine
        // scope, and stops re-applying what Windows resets -- both of which fail quietly, which
        // is the worst way to fail. Three escalating restarts, then leave it alone.
        ServiceInterop.ScAction[] actions =
        [
            new() { Type = ServiceInterop.SC_ACTION_RESTART, Delay = 5_000 },
            new() { Type = ServiceInterop.SC_ACTION_RESTART, Delay = 15_000 },
            new() { Type = ServiceInterop.SC_ACTION_RESTART, Delay = 60_000 },
        ];

        var actionSize = Marshal.SizeOf<ServiceInterop.ScAction>();
        var actionsPointer = Marshal.AllocHGlobal(actionSize * actions.Length);
        try
        {
            for (var i = 0; i < actions.Length; i++)
                Marshal.StructureToPtr(actions[i], actionsPointer + (i * actionSize), false);

            var failureActions = new ServiceInterop.ServiceFailureActions
            {
                ResetPeriod = 86_400, // seconds; forget the failure count after a day
                RebootMessage = IntPtr.Zero,
                Command = IntPtr.Zero,
                ActionCount = (uint)actions.Length,
                Actions = actionsPointer,
            };

            WithStruct(failureActions, pointer =>
            {
                if (!ServiceInterop.ChangeServiceConfig2(
                        service, ServiceInterop.SERVICE_CONFIG_FAILURE_ACTIONS, pointer))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting failure actions failed.");
            });
        }
        finally
        {
            Marshal.FreeHGlobal(actionsPointer);
        }
    }

    private static void WithStruct<T>(T value, Action<IntPtr> action) where T : struct
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            action(pointer);
        }
        finally
        {
            Marshal.DestroyStructure<T>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void WithService(Action<IntPtr> action)
    {
        var manager = OpenManager();
        try
        {
            var service = ServiceInterop.OpenService(
                manager, ServiceName, ServiceInterop.SERVICE_ALL_ACCESS);

            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw error == ServiceInterop.ERROR_SERVICE_DOES_NOT_EXIST
                    ? new InvalidOperationException("The optimizer service is not installed.")
                    : new Win32Exception(error, "OpenService failed.");
            }

            try
            {
                action(service);
            }
            finally
            {
                ServiceInterop.CloseServiceHandle(service);
            }
        }
        finally
        {
            ServiceInterop.CloseServiceHandle(manager);
        }
    }

    private static IntPtr OpenManager()
    {
        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_ALL_ACCESS);
        if (manager != IntPtr.Zero)
            return manager;

        var error = Marshal.GetLastWin32Error();
        const int ERROR_ACCESS_DENIED = 5;
        throw error == ERROR_ACCESS_DENIED
            ? new UnauthorizedAccessException("Managing the service requires administrator rights.")
            : new Win32Exception(error, "OpenSCManager failed.");
    }
}
