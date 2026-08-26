using System.ComponentModel;
using System.Runtime.InteropServices;
using Nostos.Win32.ServiceControl;

namespace Nostos.Win32.Services;

/// <summary>How a service is launched, as the SCM records it.</summary>
public enum ServiceStartType
{
    /// <summary>Loaded by the boot loader. Drivers only; never rewritten by this tool.</summary>
    Boot = 0,

    /// <summary>Started during kernel initialisation. Drivers only; never rewritten by this tool.</summary>
    System = 1,

    /// <summary>Started at boot, whether or not anything asked for it.</summary>
    Automatic = 2,

    /// <summary>Started only when something asks for it. The one this tool moves services to.</summary>
    Manual = 3,

    /// <summary>Cannot be started at all, by anything, until the start type is changed back.</summary>
    Disabled = 4,
}

/// <summary>Everything the catalog needs to know about one Windows service.</summary>
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    ServiceStartType StartType,
    bool IsRunning,
    bool DelayedAutoStart);

/// <summary>
/// Reads and rewrites service start types.
///
/// The interesting part of this class is what it refuses to do, and the line it draws is
/// narrower than it used to be. There is a difference between a service whose absence is a
/// <i>preference</i> -- no controller, no Game Pass, no Bluetooth -- and one whose absence is a
/// <i>fault</i>: no sound, no boot, no firewall. The first belongs in the catalog with a docs
/// page, whatever anyone thinks of the idea. The second is a bug report waiting to happen and
/// is refused here.
///
/// The list is enforced at the lowest level that touches the SCM rather than in the catalog,
/// so that code which never goes through a tweak cannot get around it either.
/// </summary>
public static class WindowsServices
{
    /// <summary>
    /// Services this tool will not rewrite, whatever asks it to.
    ///
    /// Two kinds of entry only:
    ///
    /// <b>The machine stops working.</b> No boot, no sign-in, no network, no sound. These are
    /// not trade-offs anybody weighed up; they are outcomes nobody wanted, discovered weeks
    /// later, with no obvious connection to a program they once ran.
    ///
    /// <b>The machine stops defending itself.</b> Turning off the firewall or the antivirus is
    /// not an optimization, and a tool that offers it as one is doing something other than what
    /// it says on the box. Windows has its own UI for that, which at least tells you afterwards.
    ///
    /// Anything that is merely unpopular, unnecessary on most machines, or a matter of taste is
    /// <b>not</b> on this list. It goes in the catalog, where it is journaled and revertible.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Protected { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Security. Turning BFE off does not "reduce overhead", it silently takes Windows
            // Firewall and IPsec with it, and the firewall UI still claims to be on.
            ["BFE"] = "Base Filtering Engine: Windows Firewall and IPsec stop filtering, while the firewall UI still reports itself as enabled.",
            ["mpssvc"] = "Windows Defender Firewall itself.",
            ["WinDefend"] = "Microsoft Defender Antivirus.",
            ["SecurityHealthService"] = "Windows Security. Disabling it hides the state of every other protection.",
            ["wscsvc"] = "Security Center, which is what tells you any of the above has been turned off.",

            // Things whose absence is immediately obvious but hard to attribute.
            ["Audiosrv"] = "Windows Audio. There is no sound at all, and no message explaining why.",
            ["AudioEndpointBuilder"] = "Windows Audio Endpoint Builder: audio devices stop being enumerated.",
            ["EventLog"] = "Windows Event Log. Diagnosing anything afterwards becomes guesswork.",
            ["RpcSs"] = "RPC. Windows does not boot.",
            ["DcomLaunch"] = "DCOM Server Process Launcher. Windows does not boot.",
            ["PlugPlay"] = "Plug and Play: new hardware stops being detected.",
            ["Power"] = "Power service: power plans and CPU throttling policy stop applying.",
            ["ProfSvc"] = "User Profile Service. Sign-in fails.",
            ["Dhcp"] = "DHCP Client. The machine loses its IP address.",
            ["Dnscache"] = "DNS Client. Name resolution stops.",
            ["NlaSvc"] = "Network Location Awareness: the firewall cannot tell which profile to apply.",
            ["nsi"] = "Network Store Interface: networking stops.",
            ["CryptSvc"] = "Cryptographic Services: Windows Update and driver signature checks fail.",
            ["BrokerInfrastructure"] = "Background Tasks Infrastructure. Store apps and the shell misbehave in ways that look like corruption.",
        };

    /// <summary>Why this service is protected, or null if it is not.</summary>
    public static string? ProtectionReason(string serviceName)
        => Protected.TryGetValue(serviceName, out var reason) ? reason : null;

    /// <summary>Reads a service's configuration, or null when no such service is registered.</summary>
    public static ServiceInfo? Query(string serviceName)
    {
        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the service control manager.");

        try
        {
            var service = ServiceInterop.OpenService(
                manager, serviceName, ServiceInterop.SERVICE_QUERY_CONFIG | ServiceInterop.SERVICE_QUERY_STATUS);

            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ServiceInterop.ERROR_SERVICE_DOES_NOT_EXIST)
                    return null;

                throw new Win32Exception(error, $"Could not open service '{serviceName}'.");
            }

            try
            {
                var config = ReadConfig(service, serviceName);

                var status = new ServiceInterop.ServiceStatus();
                var running = ServiceInterop.QueryServiceStatus(service, ref status)
                              && status.CurrentState != ServiceInterop.SERVICE_STOPPED;

                return new ServiceInfo(
                    serviceName,
                    config.DisplayName,
                    config.StartType,
                    running,
                    // Delayed auto-start is a separate config level. It only means anything for
                    // Automatic, and it is captured so revert can tell "Automatic" from
                    // "Automatic (Delayed Start)" -- which are different, and which the SCM will
                    // happily let you conflate.
                    config.StartType == ServiceStartType.Automatic && IsDelayedAutoStart(serviceName));
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

    public static bool Exists(string serviceName) => Query(serviceName) is not null;

    /// <summary>
    /// Rewrites a service's start type.
    ///
    /// Refuses protected services, and refuses to touch anything currently starting at Boot or
    /// System scope: those are drivers, the SCM will let you set them to Disabled, and the
    /// machine will not come back.
    /// </summary>
    public static void SetStartType(string serviceName, ServiceStartType startType)
    {
        if (ProtectionReason(serviceName) is { } reason)
        {
            throw new InvalidOperationException(
                $"'{serviceName}' is on the protected list and will not be modified. {reason}");
        }

        if (startType is ServiceStartType.Boot or ServiceStartType.System)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startType), startType,
                "Boot and System start are for drivers. This tool only moves services between "
                + "Automatic, Manual and Disabled.");
        }

        var current = Query(serviceName)
            ?? throw new InvalidOperationException($"No service named '{serviceName}' is registered on this machine.");

        if (current.StartType is ServiceStartType.Boot or ServiceStartType.System)
        {
            throw new InvalidOperationException(
                $"'{serviceName}' starts at {current.StartType} scope, which means it is a driver "
                + "rather than a background service. Changing it can leave the machine unbootable.");
        }

        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_ALL_ACCESS);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not open the service control manager for writing. This needs elevation.");
        }

        try
        {
            var service = ServiceInterop.OpenService(manager, serviceName, ServiceInterop.SERVICE_CHANGE_CONFIG);
            if (service == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open service '{serviceName}' for writing.");

            try
            {
                var ok = ServiceInterop.ChangeServiceConfig(
                    service,
                    ServiceInterop.SERVICE_NO_CHANGE,
                    (uint)startType,
                    ServiceInterop.SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null);

                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not set '{serviceName}' to {startType}.");
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
    /// Stops a running service, if it is running and if it can be stopped.
    ///
    /// Returns false rather than throwing when the service refuses -- something else depending
    /// on it is a reason to leave it running until the next boot, not a reason to fail an apply
    /// whose real work (the start type) already succeeded.
    /// </summary>
    public static bool TryStop(string serviceName, out string detail)
    {
        if (ProtectionReason(serviceName) is { } reason)
            throw new InvalidOperationException($"'{serviceName}' is on the protected list and will not be stopped. {reason}");

        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_ALL_ACCESS);
        if (manager == IntPtr.Zero)
        {
            detail = "could not open the service control manager";
            return false;
        }

        try
        {
            var service = ServiceInterop.OpenService(
                manager, serviceName, ServiceInterop.SERVICE_STOP | ServiceInterop.SERVICE_QUERY_STATUS);

            if (service == IntPtr.Zero)
            {
                detail = $"could not open '{serviceName}' to stop it";
                return false;
            }

            try
            {
                var status = new ServiceInterop.ServiceStatus();
                if (ServiceInterop.QueryServiceStatus(service, ref status)
                    && status.CurrentState == ServiceInterop.SERVICE_STOPPED)
                {
                    detail = "already stopped";
                    return true;
                }

                if (!ServiceInterop.ControlService(service, ServiceInterop.SERVICE_CONTROL_STOP, ref status))
                {
                    var error = Marshal.GetLastWin32Error();
                    detail = error switch
                    {
                        ServiceInterop.ERROR_SERVICE_NOT_ACTIVE => "already stopped",
                        ServiceInterop.ERROR_DEPENDENT_SERVICES_RUNNING =>
                            "still in use by another service, so it keeps running until the next reboot",
                        _ => $"stop refused (error {error}), so it keeps running until the next reboot",
                    };
                    return error == ServiceInterop.ERROR_SERVICE_NOT_ACTIVE;
                }

                detail = "stopped";
                return true;
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
    /// Sets or clears the delayed-auto-start flag.
    ///
    /// Only meaningful alongside <see cref="ServiceStartType.Automatic"/>, and only used by
    /// revert: restoring a service to plain Automatic when the user had it on Automatic
    /// (Delayed Start) would move it earlier in boot under the cover of putting it back.
    /// </summary>
    public static void SetDelayedAutoStart(string serviceName, bool delayed)
    {
        if (ProtectionReason(serviceName) is { } reason)
            throw new InvalidOperationException($"'{serviceName}' is on the protected list. {reason}");

        var manager = ServiceInterop.OpenSCManager(null, null, ServiceInterop.SC_MANAGER_ALL_ACCESS);
        if (manager == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the service control manager for writing.");

        try
        {
            var service = ServiceInterop.OpenService(manager, serviceName, ServiceInterop.SERVICE_CHANGE_CONFIG);
            if (service == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open service '{serviceName}' for writing.");

            try
            {
                var info = new ServiceInterop.ServiceDelayedAutoStartInfo { DelayedAutostart = delayed };
                var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<ServiceInterop.ServiceDelayedAutoStartInfo>());
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!ServiceInterop.ChangeServiceConfig2(
                            service, ServiceInterop.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, buffer))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            $"Could not set the delayed-auto-start flag on '{serviceName}'.");
                    }
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

    // ------------------------------------------------------------------ internals

    private static (string DisplayName, ServiceStartType StartType) ReadConfig(IntPtr service, string serviceName)
    {
        // Two passes: the first fails with the size it wanted, the second reads into it. The
        // string fields point into this buffer, so they are read before it is freed.
        ServiceInterop.QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not size the config for '{serviceName}'.");

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ServiceInterop.QueryServiceConfig(service, buffer, needed, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read the config for '{serviceName}'.");

            var config = Marshal.PtrToStructure<ServiceInterop.ServiceConfigInfo>(buffer);
            var display = config.DisplayName != IntPtr.Zero
                ? Marshal.PtrToStringUni(config.DisplayName) ?? serviceName
                : serviceName;

            return (display, (ServiceStartType)config.StartType);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Reads the delayed-auto-start flag from the registry rather than through
    /// QueryServiceConfig2, which needs a buffer dance for one boolean.
    /// </summary>
    private static bool IsDelayedAutoStart(string serviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");

            return key?.GetValue("DelayedAutostart") is int flag && flag != 0;
        }
        catch
        {
            return false;
        }
    }
}
