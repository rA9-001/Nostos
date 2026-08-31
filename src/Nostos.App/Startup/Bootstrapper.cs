using Nostos.Core.Localization;
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

    private readonly StartupStep _storage = new(Strings.Get("setup.step.data"));
    private readonly StartupStep _profiles = new(Strings.Get("setup.step.profiles"));
    private readonly StartupStep _service = new(Strings.Get("setup.step.service"));
    private readonly StartupStep _connect = new(Strings.Get("setup.step.connect"));

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
    /// A profile the user has edited is never overwritten, and one they deleted on purpose does
    /// not come back. Both of those used to fall out of running only while the folder was
    /// entirely empty, which also meant a profile improved in a later release reached nobody
    /// who had ever run the app -- so instead each file is decided on its own, against a record
    /// of what was last written. See <see cref="ShippedState"/>.
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

            RetireSupersededProfiles(target);

            var assembly = Assembly.GetExecutingAssembly();
            var bundled = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(BundledProfilePrefix, StringComparison.Ordinal))
                .ToList();

            if (bundled.Count == 0)
            {
                _profiles.Status = StepStatus.Skipped;
                _profiles.Detail = Strings.Get("setup.profiles.none.shipped");
                return;
            }

            var state = ShippedState.Load();
            var copied = 0;

            foreach (var name in bundled)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                    continue;

                var fileName = name[BundledProfilePrefix.Length..];
                var destination = Path.Combine(target, fileName);

                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bundledBytes = buffer.ToArray();
                var bundledHash = ShippedState.HashOf(bundledBytes);

                if (File.Exists(destination))
                {
                    // Three cases, and only one of them writes.
                    //
                    // The file is already what we ship: nothing to do. The file is what we last
                    // wrote but we now ship something different: replace it, because that is a
                    // profile improved in a release, and the alternative is that the improvement
                    // reaches only people installing for the first time. The file is neither:
                    // the user has edited it, and it is theirs.
                    var currentHash = ShippedState.HashOf(File.ReadAllBytes(destination));
                    if (currentHash == bundledHash)
                    {
                        state.Record(fileName, bundledHash);
                        continue;
                    }

                    if (!state.WasWrittenByUs(fileName, currentHash))
                        continue;
                }

                // Written to a temporary name and moved into place, so an interrupted first run
                // cannot leave a half-written profile behind. It is worth the two extra lines:
                // the file is only ever written when the folder is empty, so a zero-byte one
                // survives forever, and every launch afterwards fails to parse it and reports
                // "could not start" over a catalog that is actually fine. Found by killing the
                // app mid-startup while timing launches.
                var temporary = destination + ".tmp";

                File.WriteAllBytes(temporary, bundledBytes);
                File.Move(temporary, destination, overwrite: true);
                state.Record(fileName, bundledHash);
                copied++;
            }

            state.Save();

            if (copied > 0)
            {
                _profiles.Status = StepStatus.Fixed;
                _profiles.Detail = Strings.Format("setup.profiles.installed", copied);
            }
            else
            {
                _profiles.Status = StepStatus.Ok;
                _profiles.Detail = Strings.Format(
                    "setup.profiles.existing", Directory.EnumerateFiles(target, "*.json").Count());
            }
        }
        catch (Exception e)
        {
            _profiles.Status = StepStatus.Failed;
            _profiles.Detail = e.Message;
        }
    }

    /// <summary>
    /// Profiles this program used to ship and no longer does.
    ///
    /// "conservative", "competitive" and "streaming" were three different goals; they are now
    /// three rungs of one ladder, which is a different idea and not a rename. Leaving the old
    /// files in place would show six profiles, three of them describing a scheme that no longer
    /// exists.
    /// </summary>
    private static readonly string[] SupersededProfiles = ["conservative", "competitive", "streaming"];

    /// <summary>
    /// Moves a superseded profile aside rather than deleting it.
    ///
    /// They are ours and nobody is expected to miss them, but a profile is a file the user is
    /// invited to read, copy and edit, and some of them will have been. Deleting somebody's
    /// edited copy to tidy up after a rename is a larger act than this program is entitled to;
    /// renaming it stops the loader seeing it -- it reads *.json -- and costs nothing to undo.
    /// </summary>
    private static void RetireSupersededProfiles(string directory)
    {
        foreach (var name in SupersededProfiles)
        {
            var path = Path.Combine(directory, name + ".json");
            if (!File.Exists(path))
                continue;

            try
            {
                File.Move(path, path + ".superseded", overwrite: true);
            }
            catch (IOException)
            {
                // Not worth failing a launch over. The worst case is an extra row in the list.
            }
        }
    }

    private async Task<string?> EnsureServiceAsync(CancellationToken ct)
    {
        _service.Status = StepStatus.Running;

        if (AppPaths.IsPortable)
        {
            _service.Status = StepStatus.Skipped;
            _service.Detail = Strings.Get("setup.service.portable");
            return Strings.Get("notice.portable");
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
                RegistrationHealth.Matches => Strings.Get("setup.service.running"),
                RegistrationHealth.OtherCopy => Strings.Get("setup.service.othercopy"),
                _ => Strings.Get("setup.service.orphaned"),
            };
            _needsRepair = registration == RegistrationHealth.Orphaned;
            return NoticeFor(registration);
        }

        if (!ForceServiceSetup && HasDeclined())
        {
            _service.Status = StepStatus.Skipped;
            _service.Detail = Strings.Get("setup.service.declined");
            return Strings.Get("notice.service.declined");
        }

        var executable = FindServiceExecutable();
        if (executable is null)
        {
            _service.Status = StepStatus.Failed;
            _service.Detail = Strings.Get("setup.service.missingexe");
            return Strings.Get("notice.service.incomplete");
        }

        // A single elevated invocation installs AND starts, so the user sees one prompt.
        var elevated = await RunElevatedSetupAsync(executable, ct).ConfigureAwait(false);

        if (!elevated)
        {
            RecordDecline();
            _service.Status = StepStatus.Skipped;
            _service.Detail = Strings.Get("setup.service.uacdeclined");
            return Strings.Get("notice.service.uacdeclined");
        }

        // The SCM reports RUNNING before the daemon has necessarily opened its pipe.
        for (var attempt = 0; attempt < 20 && ServiceInstaller.QueryState() != ServiceState.Running; attempt++)
            await Task.Delay(250, ct).ConfigureAwait(false);

        var installed = ServiceInstaller.QueryState() == ServiceState.Running;
        _service.Status = installed ? StepStatus.Fixed : StepStatus.Failed;
        _service.Detail = installed
            ? registration == RegistrationHealth.Matches ? Strings.Get("setup.service.installed") : Strings.Get("setup.service.repaired")
            : Strings.Get("setup.service.notrunning");

        return installed ? null : Strings.Get("notice.service.notrunning");
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
                Strings.Format("notice.service.othercopy", folder),

            // This one is a genuine time bomb: it works now and stops working at the next
            // reboot, which is the hardest kind of failure to connect back to its cause.
            RegistrationHealth.Orphaned =>
                Strings.Format("notice.service.orphaned", folder),

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
            return Strings.Format(unpackFailure, NativeAssets.WarningDetail);

        if (SystemInfo.SmartAppControl == SmartAppControlState.Enforced)
        {
            return Strings.Get("notice.smartappcontrol");
        }

        return null;
    }
}
