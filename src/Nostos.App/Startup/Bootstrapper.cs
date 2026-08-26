using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Nostos.App.Backends;
using Nostos.Core;
using Nostos.Ipc;
using Nostos.Win32.ServiceControl;
using Nostos.Win32.Services;

namespace Nostos.App.Startup;

/// <param name="Backend">The backend the app should use for this session.</param>
/// <param name="ServiceReady">True when the privileged service is installed, running and reachable.</param>
/// <param name="Notice">Non-fatal thing the user should know, or null.</param>
/// <param name="ServiceNeedsRepair">
/// True when the service is reachable but its registration is broken -- it works now and will
/// not survive a restart. The UI offers a repair rather than only reporting it.
/// </param>
public sealed record BootstrapResult(
    IOptimizerBackend Backend, bool ServiceReady, string? Notice, bool ServiceNeedsRepair = false);

/// <summary>
/// Everything that used to be a README instruction, done automatically at launch.
///
/// The design rule here is that the app never asks the user to go and run something else.
/// It creates its own folders, seeds its own default profiles, installs and starts its own
/// service, and if any of that fails it degrades to a working direct mode and explains itself.
/// The one thing it cannot do silently is elevate — so it asks exactly once, remembers a
/// refusal, and never nags again.
/// </summary>
public sealed class Bootstrapper
{
    /// <summary>Resource-name prefix under which the default profiles are embedded.</summary>
    private const string BundledProfilePrefix = "Nostos.profiles.";

    /// <summary>Written next to the data when the user declines the service, so we stop asking.</summary>
    private static string DeclineMarkerPath => Path.Combine(AppPaths.Root, "service-declined.json");

    public ObservableCollection<StartupStep> Steps { get; } = [];

    private readonly StartupStep _storage = new("Preparing data folder");
    private readonly StartupStep _profiles = new("Checking built-in profiles");
    private readonly StartupStep _service = new("Setting up the background service");
    private readonly StartupStep _connect = new("Connecting");

    /// <summary>Set by the service step, reported by the connect step.</summary>
    private bool _needsRepair;

    public Bootstrapper()
    {
        Steps.Add(_storage);
        Steps.Add(_profiles);
        Steps.Add(_service);
        Steps.Add(_connect);
    }

    /// <summary>Set when the user is re-running setup on purpose, overriding a past refusal.</summary>
    public bool ForceServiceSetup { get; init; }

    public async Task<BootstrapResult> RunAsync(CancellationToken ct = default)
    {
        string? notice = null;

        PrepareStorage();
        SeedProfiles();
        notice = await EnsureServiceAsync(ct).ConfigureAwait(false) ?? notice;

        return await ConnectAsync(notice, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ steps

    private void PrepareStorage()
    {
        _storage.Status = StepStatus.Running;
        try
        {
            var existed = Directory.Exists(AppPaths.Root);
            AppPaths.EnsureCreated();

            _storage.Detail = AppPaths.Root;
            _storage.Status = existed ? StepStatus.Ok : StepStatus.Fixed;
        }
        catch (Exception e)
        {
            _storage.Status = StepStatus.Failed;
            _storage.Detail = e.Message;
        }
    }

    /// <summary>
    /// Writes the profiles shipped with the app into the data folder when it has none.
    ///
    /// Only fills the gap once: a profile the user has edited is never overwritten, and one
    /// they deleted on purpose does not come back, because this runs only while the folder is
    /// entirely empty.
    ///
    /// The defaults are embedded in the assembly rather than sitting in a folder next to the
    /// executable, so that a single-file build ships with them too.
    /// </summary>
    private void SeedProfiles()
    {
        _profiles.Status = StepStatus.Running;
        try
        {
            var target = AppPaths.ProfilesDirectory;
            Directory.CreateDirectory(target);

            var existing = Directory.EnumerateFiles(target, "*.json").Count();
            if (existing > 0)
            {
                _profiles.Status = StepStatus.Ok;
                _profiles.Detail = $"{existing} profile(s)";
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var bundled = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(BundledProfilePrefix, StringComparison.Ordinal))
                .ToList();

            if (bundled.Count == 0)
            {
                _profiles.Status = StepStatus.Skipped;
                _profiles.Detail = "this build ships no default profiles";
                return;
            }

            var copied = 0;
            foreach (var name in bundled)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                    continue;

                var fileName = name[BundledProfilePrefix.Length..];
                using var file = File.Create(Path.Combine(target, fileName));
                stream.CopyTo(file);
                copied++;
            }

            _profiles.Status = copied > 0 ? StepStatus.Fixed : StepStatus.Ok;
            _profiles.Detail = copied > 0 ? $"installed {copied} default profile(s)" : "none to install";
        }
        catch (Exception e)
        {
            _profiles.Status = StepStatus.Failed;
            _profiles.Detail = e.Message;
        }
    }

    private async Task<string?> EnsureServiceAsync(CancellationToken ct)
    {
        _service.Status = StepStatus.Running;

        if (AppPaths.IsPortable)
        {
            _service.Status = StepStatus.Skipped;
            _service.Detail = "portable mode: running without the background service";
            return "Portable mode. Machine-wide tweaks need an elevated launch; everything " +
                   "user-scoped works as it is.";
        }

        var state = ServiceInstaller.QueryState();
        var registration = InspectRegistration();

        // A healthy running service is left alone. A broken registration is only repaired when
        // the user asks for it, because repairing means another elevation prompt and the app
        // works fine in the meantime.
        if (state == ServiceState.Running && (registration == RegistrationHealth.Matches || !ForceServiceSetup))
        {
            _service.Status = StepStatus.Ok;
            _service.Detail = registration switch
            {
                RegistrationHealth.Matches => "already running",
                RegistrationHealth.OtherCopy => "running from another copy of the app",
                _ => "running, but registered to a folder that is gone",
            };
            _needsRepair = registration == RegistrationHealth.Orphaned;
            return NoticeFor(registration);
        }

        if (!ForceServiceSetup && HasDeclined())
        {
            _service.Status = StepStatus.Skipped;
            _service.Detail = "previously declined";
            return "The background service is not installed, so machine-wide tweaks are " +
                   "unavailable. You can enable it at any time.";
        }

        var executable = FindServiceExecutable();
        if (executable is null)
        {
            _service.Status = StepStatus.Failed;
            _service.Detail = "Nostos.Service.exe is missing from this folder";
            return "This copy is incomplete: the service executable is missing, so only " +
                   "user-scope tweaks are available.";
        }

        // A single elevated invocation installs AND starts, so the user sees one prompt.
        var elevated = await RunElevatedSetupAsync(executable, ct).ConfigureAwait(false);

        if (!elevated)
        {
            RecordDecline();
            _service.Status = StepStatus.Skipped;
            _service.Detail = "administrator approval declined";
            return "Continuing without the background service. Machine-wide tweaks stay " +
                   "unavailable until it is installed.";
        }

        // The SCM reports RUNNING before the daemon has necessarily opened its pipe.
        for (var attempt = 0; attempt < 20 && ServiceInstaller.QueryState() != ServiceState.Running; attempt++)
            await Task.Delay(250, ct).ConfigureAwait(false);

        var installed = ServiceInstaller.QueryState() == ServiceState.Running;
        _service.Status = installed ? StepStatus.Fixed : StepStatus.Failed;
        _service.Detail = installed
            ? registration == RegistrationHealth.Matches ? "installed and started" : "repaired and started"
            : "setup ran but the service is not running";

        return installed ? null : "The service was set up but is not running. Continuing directly.";
    }

    private async Task<BootstrapResult> ConnectAsync(string? notice, CancellationToken ct)
    {
        _connect.Status = StepStatus.Running;

        // Portable mode never uses the service, even when one is installed and running on this
        // machine. It would work -- but the service journals to %ProgramData%, so half of what
        // this copy changed would be recorded somewhere the folder does not carry with it, and
        // "the folder holds the record of what it changed" is the whole promise of portable.
        if (AppPaths.IsPortable)
        {
            var portable = new LocalBackend();
            _connect.Status = StepStatus.Ok;
            _connect.Detail = portable.Description;
            return new BootstrapResult(portable, false, notice);
        }

        // Do not spend the pipe's connect timeout discovering something the SCM can answer
        // instantly. Without this, every launch on a machine with no service stalls for three
        // seconds on a connection that was never going to succeed.
        if (_service.Status is StepStatus.Skipped or StepStatus.Failed
            && ServiceInstaller.QueryState() != ServiceState.Running)
        {
            var direct = new LocalBackend();
            _connect.Status = StepStatus.Ok;
            _connect.Detail = direct.Description;
            return new BootstrapResult(direct, false, notice, _needsRepair);
        }

        // Give the freshly started daemon a moment to accept connections before deciding it
        // is unreachable; a first-run failure here would be purely a race.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                var service = await ServiceBackend.ConnectAsync(ct).ConfigureAwait(false);

                // User-scoped tweaks are done in this process, because SYSTEM has its own user
                // hive and the service would otherwise read and write the wrong one.
                var backend = new SplitBackend(service, new LocalBackend());

                _connect.Status = StepStatus.Ok;
                _connect.Detail = backend.Description;
                return new BootstrapResult(backend, true, notice, _needsRepair);
            }
            catch (ServiceUnavailableException) when (attempt < 11 && _service.Status == StepStatus.Fixed)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }
        }

        var local = new LocalBackend();
        _connect.Status = StepStatus.Ok;
        _connect.Detail = local.Description;

        return new BootstrapResult(local, false, notice, _needsRepair);
    }

    /// <summary>How the registered service relates to the copy of the app that is running.</summary>
    private enum RegistrationHealth
    {
        /// <summary>Not installed, or installed from this folder. Nothing to say.</summary>
        Matches,

        /// <summary>Installed from a different folder that still exists.</summary>
        OtherCopy,

        /// <summary>Installed from a folder that has since been moved or deleted.</summary>
        Orphaned,
    }

    private static RegistrationHealth InspectRegistration()
    {
        var registered = ServiceInstaller.QueryBinaryPath();
        if (registered is null)
            return RegistrationHealth.Matches;

        if (!File.Exists(registered))
            return RegistrationHealth.Orphaned;

        var ours = FindServiceExecutable();
        if (ours is null)
            return RegistrationHealth.Matches;

        try
        {
            return string.Equals(Path.GetFullPath(registered), Path.GetFullPath(ours),
                StringComparison.OrdinalIgnoreCase)
                ? RegistrationHealth.Matches
                : RegistrationHealth.OtherCopy;
        }
        catch (ArgumentException)
        {
            return RegistrationHealth.Matches;
        }
    }

    private static string? NoticeFor(RegistrationHealth registration)
    {
        var folder = Path.GetDirectoryName(ServiceInstaller.QueryBinaryPath() ?? string.Empty);

        return registration switch
        {
            // Worth saying, because the service running your changes is a build you may not
            // have meant to be in charge -- but nothing is broken, so this is not an alarm.
            RegistrationHealth.OtherCopy =>
                $"The background service is running from another copy of the app ({folder}). " +
                "Changes made here still go through it.",

            // This one is a genuine time bomb: it works now and stops working at the next
            // reboot, which is the hardest kind of failure to connect back to its cause.
            RegistrationHealth.Orphaned =>
                $"The background service is registered to a folder that no longer exists ({folder}), " +
                "so it will not come back after a restart. Use Enable background service to repair it.",

            _ => null,
        };
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// Locates the service executable next to this one, falling back to the sibling build
    /// output so that running from a development tree behaves the same as a real install.
    /// </summary>
    public static string? FindServiceExecutable()
    {
        const string name = "Nostos.Service.exe";

        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside))
            return beside;

        // bin/<Config>/<Tfm>/ -> ../../../../Nostos.Service/bin/<Config>/<Tfm>/
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = directory.Name;
        var configuration = directory.Parent?.Name;
        var sourceRoot = directory.Parent?.Parent?.Parent?.Parent;

        if (configuration is null || sourceRoot is null)
            return null;

        var developmentPath = Path.Combine(
            sourceRoot.FullName, "Nostos.Service", "bin", configuration, tfm, name);

        return File.Exists(developmentPath) ? developmentPath : null;
    }

    private static async Task<bool> RunElevatedSetupAsync(string executable, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            // ShellExecute + runas raises the UAC prompt. The app itself stays asInvoker: it
            // must never run elevated, only ask a child to.
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("setup");
        // Pass the SID explicitly: the elevated child may run under a different account, and
        // the pipe has to admit the account that is actually using the app.
        startInfo.ArgumentList.Add(ServiceConfiguration.CurrentUserSid());

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user clicked No.
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HasDeclined() => File.Exists(DeclineMarkerPath);

    private static void RecordDecline()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(DeclineMarkerPath, JsonSerializer.Serialize(
                new ServiceDeclineMarker(
                    DateTimeOffset.UtcNow,
                    "Delete this file, or use Enable background service in the app, to be asked again."),
                AppJsonContext.Default.ServiceDeclineMarker));
        }
        catch (IOException)
        {
            // Not being able to remember the refusal is better than failing to start.
        }
    }

    public static void ClearDecline()
    {
        try
        {
            if (File.Exists(DeclineMarkerPath))
                File.Delete(DeclineMarkerPath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Environment facts worth telling the user about, gathered once at startup.</summary>
    public static string? EnvironmentWarning()
    {
        if (NativeAssets.Warning is { } unpackFailure)
            return unpackFailure;

        if (SystemInfo.SmartAppControl == SmartAppControlState.Enforced)
        {
            return "Smart App Control is enforced on this machine. Unsigned builds are blocked " +
                   "from running, so parts of this app may not start until it is signed.";
        }

        return null;
    }
}
