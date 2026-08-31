using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Service;
using Nostos.Service.Daemon;
using Nostos.Service.Logging;
using Nostos.Win32.Removal;
using Nostos.Win32.ServiceControl;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";

switch (command)
{
    // One elevated invocation that leaves the service installed AND running. This is what the
    // desktop app launches when it sets itself up, so the user sees a single UAC prompt for
    // the life of the product rather than one per step.
    case "setup":
        return Guarded(() =>
        {
            var self = Environment.ProcessPath!;
            var registered = ServiceInstaller.QueryBinaryPath();
            var repointing = registered is not null && !SamePath(registered, self);

            // A running service goes on executing the binary it was started with, so a repoint
            // that skipped this would report success and change nothing until the next reboot.
            if (repointing && ServiceInstaller.QueryState() == ServiceState.Running)
                ServiceInstaller.Stop();

            // Idempotent: registers the service, or re-points and re-configures an existing
            // registration, and merges the caller's SID into the allow list either way. That is
            // how a second user on the machine gets access to the pipe.
            ServiceInstaller.Install(self, args.ElementAtOrDefault(1));

            Console.WriteLine(
                registered is null ? $"Installed '{ServiceInstaller.ServiceName}'."
                : repointing ? $"Re-pointed '{ServiceInstaller.ServiceName}' at {self}"
                : "Already installed.");

            ServiceInstaller.Start();
            Console.WriteLine($"State: {ServiceInstaller.QueryState()}");
            return 0;
        });

    case "install":
        return Guarded(() =>
        {
            ServiceInstaller.Install(Environment.ProcessPath!);
            Console.WriteLine($"Registered '{ServiceInstaller.ServiceName}'.");
            Console.WriteLine($"  Binary:  {Environment.ProcessPath}");
            Console.WriteLine($"  Account: LocalSystem, delayed auto-start");
            Console.WriteLine($"  Config:  {ServiceConfiguration.Path}");
            return 0;
        });

    case "uninstall":
        return Guarded(() =>
        {
            ServiceInstaller.Uninstall();
            Console.WriteLine($"Uninstalled '{ServiceInstaller.ServiceName}'.");
            Console.WriteLine();
            Console.WriteLine("Tweaks already applied to this machine were NOT reverted -- removing the");
            Console.WriteLine("service is not the same as undoing its changes. Run `nos revert --all`");
            Console.WriteLine("first if that is what you wanted; the journal is still on disk either way.");
            return 0;
        });

    // The elevated half of "remove Nostos from this PC". Launched by the app's Settings panel,
    // which has already reverted every tweak through the ordinary revert path before getting
    // here -- this verb is only about the two things an ordinary user cannot delete: a service
    // registration, and files the service wrote to %ProgramData% as LocalSystem.
    //
    // It takes no arguments on purpose. An elevated process that deletes a directory named by
    // an unelevated caller is an arbitrary-delete primitive wearing a helpful hat; the only
    // path this will ever touch is the one it computes for itself.
    case "remove":
        return Guarded(() =>
        {
            var root = AppPaths.Root;

            ServiceInstaller.Uninstall();
            Console.WriteLine($"Removed '{ServiceInstaller.ServiceName}'.");

            var leftovers = new List<string>();
            if (Directory.Exists(root))
            {
                SystemRemoval.DeleteTree(root, leftovers);
                Console.WriteLine(Directory.Exists(root)
                    ? $"Could not fully delete {root}: {leftovers.Count} file(s) remain."
                    : $"Deleted {root}.");
            }

            foreach (var leftover in leftovers)
                Console.Error.WriteLine($"  left behind: {leftover}");

            // Not an error exit. The service is gone, which is the part only this process could
            // do; the caller re-reads the disk and reports the rest itself.
            return 0;
        });

    case "start":
        return Guarded(() =>
        {
            ServiceInstaller.Start();
            Console.WriteLine($"Started. State: {ServiceInstaller.QueryState()}");
            return 0;
        });

    case "stop":
        return Guarded(() =>
        {
            ServiceInstaller.Stop();
            Console.WriteLine($"Stopped. State: {ServiceInstaller.QueryState()}");
            return 0;
        });

    // Called by the unelevated app once it has downloaded and verified an update. The app can
    // do everything except this: the service holds its own executable open and only an
    // administrator can stop it, so the whole privileged half is one prompt and one verb.
    //
    // Everything this receives has already been checked against a signature the app trusts. It
    // is still handed a directory path by another process, so it re-checks the shape of what is
    // there before it starts overwriting an installation.
    case "apply-update":
        return Guarded(() =>
        {
            var staged = args.ElementAtOrDefault(1);
            if (string.IsNullOrWhiteSpace(staged) || !Directory.Exists(staged))
            {
                Console.Error.WriteLine("usage: Nostos.Service.exe apply-update <staged-directory>");
                return 2;
            }

            var self = Environment.ProcessPath!;
            var target = Path.GetDirectoryName(self)!;

            // A staged folder with no app in it is not an update, and copying it over an
            // installation would produce a broken one.
            if (!File.Exists(Path.Combine(staged, "Nostos.exe"))
                || !File.Exists(Path.Combine(staged, "Nostos.Service.exe")))
            {
                Console.Error.WriteLine($"'{staged}' does not look like a Nostos build; refusing.");
                return 2;
            }

            var wasRunning = ServiceInstaller.QueryState() == ServiceState.Running;
            if (wasRunning)
            {
                Console.WriteLine("Stopping the service...");
                ServiceInstaller.Stop();
            }

            var replaced = 0;
            foreach (var source in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staged, source);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                // This process is running from the folder being overwritten, so its own image
                // cannot be replaced -- only renamed out of the way. The leftovers are swept up
                // by the app on its next launch.
                if (File.Exists(destination))
                {
                    try
                    {
                        File.Delete(destination);
                    }
                    catch (IOException)
                    {
                        var displaced = destination + ".old";
                        if (File.Exists(displaced))
                            try { File.Delete(displaced); } catch (IOException) { }
                        File.Move(destination, displaced);
                    }
                }

                File.Copy(source, destination, overwrite: true);
                replaced++;
            }

            Console.WriteLine($"Replaced {replaced} file(s) in {target}.");

            // Re-register rather than only starting. The binary path recorded by the SCM is
            // absolute, and re-pointing it at the same place is free, while skipping it would
            // leave a moved installation pointing at a folder that no longer exists.
            ServiceInstaller.Install(self);

            if (wasRunning)
            {
                ServiceInstaller.Start();
                Console.WriteLine($"State: {ServiceInstaller.QueryState()}");
            }

            try
            {
                Directory.Delete(staged, recursive: true);
            }
            catch (IOException)
            {
                // Scratch space inside the app's own data folder. Leaving it costs disk, not
                // correctness, and the next update overwrites it.
            }

            return 0;
        });

    case "status":
        Console.WriteLine($"Service:  {ServiceInstaller.QueryState()}");
        Console.WriteLine($"Config:   {(File.Exists(ServiceConfiguration.Path) ? ServiceConfiguration.Path : "(defaults)")}");
        return 0;

    case "run":
        return RunService();

    case "--console":
    case "console":
        return await RunConsoleAsync();

    default:
        PrintUsage();
        return command is "help" or "--help" or "-h" ? 0 : 2;
}

// Launched by the Service Control Manager.
static int RunService()
{
    using var log = new FileLogSink(echoToConsole: false);
    var configuration = ServiceConfiguration.Load();
    var daemon = new OptimizerDaemon(configuration, log);
    var host = new WindowsServiceHost(daemon.RunAsync, log);

    try
    {
        if (host.RunAsService())
            return 0;

        // Not started by the SCM. Say so rather than silently doing nothing, which is the
        // failure mode people spend an afternoon on.
        Console.Error.WriteLine(
            "This process was not launched by the Service Control Manager.\n" +
            "Use '--console' to run the daemon in the foreground, or 'setup' to install it.");
        return 2;
    }
    catch (Exception e)
    {
        log.Error("the service host failed", e);
        return 1;
    }
}

// Foreground mode: same daemon, no SCM, no elevation ceremony. This is how the pipe and the
// reconciler get exercised during development.
static async Task<int> RunConsoleAsync()
{
    using var log = new FileLogSink(echoToConsole: true, minimum: LogLevel.Debug);
    var configuration = ServiceConfiguration.Load();

    if (configuration.AllowedSids.Count == 0)
    {
        // Without this, a console run would create a pipe only SYSTEM and admins can reach,
        // and every client connection would fail with an unhelpful access-denied.
        configuration = configuration with { AllowedSids = [ServiceConfiguration.CurrentUserSid()] };
        log.Warn("no service.json found; allowing the current user on the control pipe for this run");
    }

    using var stopping = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.Error.WriteLine();
        log.Info("Ctrl+C received, shutting down");
        stopping.Cancel();
    };

    var daemon = new OptimizerDaemon(configuration, log);

    try
    {
        await daemon.RunAsync(stopping.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        return 0;
    }
    catch (Exception e)
    {
        log.Error("the daemon faulted", e);
        return 1;
    }
}

// Service paths come from the SCM and from Environment.ProcessPath, which can spell the same
// file differently. Comparing the resolved full paths keeps a harmless spelling difference from
// looking like a foreign installation.
static bool SamePath(string left, string right)
{
    try
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static int Guarded(Func<int> action)
{
    try
    {
        return action();
    }
    catch (UnauthorizedAccessException e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"error  {e.Message}");
        return 1;
    }
}

static void PrintUsage()
{
    Console.WriteLine("""

        Nostos.Service - the privileged half of Nostos

        You do not normally run this by hand. Launching the desktop app sets it up for you.

        Commands
          setup         Install and start in one step. What the app runs, elevated.
          install       Register the service without starting it.
          uninstall     Stop and remove the service. Does NOT revert applied tweaks.
          remove        Remove the service AND the data folder. The elevated half of the
                        app's "Remove Nostos from this PC"; reverts nothing by itself.
          start / stop  Control the installed service.
          status        Report whether the service is installed and running.
          run           Entry point used by the Service Control Manager.
          --console     Run the daemon in the foreground, for development.

        """);
}
