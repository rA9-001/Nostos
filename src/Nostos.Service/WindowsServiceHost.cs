using System.ComponentModel;
using System.Runtime.InteropServices;
using Nostos.Core.Abstractions;
using Nostos.Win32.ServiceControl;

namespace Nostos.Service;

/// <summary>
/// Bridges the Service Control Manager to an ordinary async workload.
///
/// The SCM contract is unforgiving in one specific way: the dispatcher thread must report
/// RUNNING quickly and must not return from ServiceMain until the work is finished, or Windows
/// declares the service hung and kills it. Everything here exists to honour that.
/// </summary>
public sealed class WindowsServiceHost
{
    /// <summary>Shared with the installer so the registered name and the dispatched name cannot drift.</summary>
    public static string ServiceName => ServiceInstaller.ServiceName;

    private readonly Func<CancellationToken, Task> _workload;
    private readonly ILogSink _log;
    private readonly CancellationTokenSource _stopping = new();

    // Held in fields so the GC cannot collect the delegates while native code holds pointers
    // to them. This is the classic way to crash a hand-written service.
    private readonly ServiceInterop.ServiceMainCallback _serviceMain;
    private readonly ServiceInterop.ServiceControlHandler _controlHandler;

    private IntPtr _statusHandle;
    private uint _checkPoint = 1;
    private Task? _work;

    public WindowsServiceHost(Func<CancellationToken, Task> workload, ILogSink log)
    {
        _workload = workload;
        _log = log;
        _serviceMain = ServiceMain;
        _controlHandler = HandleControl;
    }

    /// <summary>
    /// Hands this process to the SCM. Blocks until the service stops.
    /// Returns false when the process was not launched by the SCM, which is how
    /// <c>--console</c> mode detects that it should just run the workload directly.
    /// </summary>
    public bool RunAsService()
    {
        var table = new[]
        {
            new ServiceInterop.ServiceTableEntry
            {
                ServiceName = ServiceName,
                ServiceProc = Marshal.GetFunctionPointerForDelegate(_serviceMain),
            },
            // The SCM requires a null-terminating entry.
            new ServiceInterop.ServiceTableEntry { ServiceName = null, ServiceProc = IntPtr.Zero },
        };

        if (ServiceInterop.StartServiceCtrlDispatcher(table))
            return true;

        const int ERROR_FAILED_SERVICE_CONTROLLER_CONNECT = 1063;
        var error = Marshal.GetLastWin32Error();
        if (error == ERROR_FAILED_SERVICE_CONTROLLER_CONNECT)
            return false;

        throw new Win32Exception(error, "StartServiceCtrlDispatcher failed.");
    }

    private void ServiceMain(int argc, IntPtr argv)
    {
        _statusHandle = ServiceInterop.RegisterServiceCtrlHandlerEx(ServiceName, _controlHandler, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero)
        {
            // Nothing to report status to; the SCM will notice the process exit.
            _log.Error("RegisterServiceCtrlHandlerEx failed; the service cannot report status.");
            return;
        }

        Report(ServiceInterop.SERVICE_START_PENDING, waitHintMs: 30_000);

        try
        {
            _work = _workload(_stopping.Token);
            Report(ServiceInterop.SERVICE_RUNNING);

            // ServiceMain must not return while the service is running.
            _work.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception e)
        {
            _log.Error("the service workload faulted", e);
            Report(ServiceInterop.SERVICE_STOPPED, win32ExitCode: 1);
            return;
        }

        Report(ServiceInterop.SERVICE_STOPPED);
    }

    private uint HandleControl(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case ServiceInterop.SERVICE_CONTROL_STOP:
            case ServiceInterop.SERVICE_CONTROL_SHUTDOWN:
                // Generous wait hint: an in-flight revert must be allowed to finish rather than
                // leave the machine half-changed because the SCM lost patience.
                Report(ServiceInterop.SERVICE_STOP_PENDING, waitHintMs: 20_000);
                _stopping.Cancel();
                break;

            case ServiceInterop.SERVICE_CONTROL_INTERROGATE:
                Report(_work is { IsCompleted: false }
                    ? ServiceInterop.SERVICE_RUNNING
                    : ServiceInterop.SERVICE_STOPPED);
                break;
        }

        return 0; // NO_ERROR
    }

    private void Report(uint state, uint waitHintMs = 0, uint win32ExitCode = 0)
    {
        if (_statusHandle == IntPtr.Zero)
            return;

        var status = new ServiceInterop.ServiceStatus
        {
            ServiceType = ServiceInterop.SERVICE_WIN32_OWN_PROCESS,
            CurrentState = state,
            ControlsAccepted = state == ServiceInterop.SERVICE_RUNNING
                ? ServiceInterop.SERVICE_ACCEPT_STOP | ServiceInterop.SERVICE_ACCEPT_SHUTDOWN
                : 0,
            Win32ExitCode = win32ExitCode,
            ServiceSpecificExitCode = 0,
            // Pending states must advance a checkpoint each report or the SCM assumes a hang.
            CheckPoint = state is ServiceInterop.SERVICE_START_PENDING or ServiceInterop.SERVICE_STOP_PENDING
                ? _checkPoint++
                : 0,
            WaitHint = waitHintMs,
        };

        ServiceInterop.SetServiceStatus(_statusHandle, ref status);
    }
}
