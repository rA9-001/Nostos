using System.Diagnostics;
using Nostos.Ipc;
using Nostos.Win32.ServiceControl;

namespace Nostos.Cli.Commands;

/// <summary>
/// `nos service ...` — manage and inspect the privileged half.
///
/// Install, uninstall, start and stop are delegated to the service executable itself rather
/// than reimplemented here, and launched with the runas verb so the user gets the standard
/// UAC prompt instead of a permission error they have to decode.
/// </summary>
public static class ServiceCommands
{
    private const string ServiceExeName = "Nostos.Service.exe";

    public static async Task<int> RunAsync(CommandLine commandLine, CancellationToken ct)
    {
        var sub = commandLine.Positional.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "status";

        return sub switch
        {
            "status" => await StatusAsync(ct),
            // "install" is an alias for the one-shot setup the app uses: install and start in
            // a single elevated step, so the CLI and the GUI cannot drift apart.
            "install" => Elevate("setup"),
            "setup" or "uninstall" or "start" or "stop" => Elevate(sub),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: nos service status | install | uninstall | start | stop");
        return 2;
    }

    private static async Task<int> StatusAsync(CancellationToken ct)
    {
        Console.WriteLine();

        try
        {
            await using var client = await OptimizerClient.ConnectAsync(ct: ct);
            var ping = await client.PingAsync(ct);

            Console.WriteLine($"{Ansi.Green}Service is running{Ansi.Reset} and accepted this account.");
            Console.WriteLine($"  Version            {ping.ServiceVersion} (protocol v{ping.ProtocolVersion})");
            Console.WriteLine($"  Process id         {ping.ProcessId}");
            Console.WriteLine($"  Catalog            {ping.CatalogSize} tweaks");
            Console.WriteLine($"  Outstanding        {ping.OutstandingChanges} change(s)");

            Console.WriteLine();
            return 0;
        }
        catch (ServiceUnavailableException e)
        {
            Console.WriteLine($"{Ansi.Yellow}Service not reachable.{Ansi.Reset} {e.Message}");
            Console.WriteLine();
            Console.WriteLine($"  Registered  {ServiceInstaller.QueryState()}");
            Console.WriteLine($"  Executable  {FindServiceExecutable() ?? "(not found next to nos.exe)"}");
            Console.WriteLine("  Install     nos service install");
            Console.WriteLine();
            Console.WriteLine("The CLI works without the service; it applies changes directly instead.");
            return 1;
        }
    }

    private static int Elevate(string verb)
    {
        var executable = FindServiceExecutable();
        if (executable is null)
        {
            Console.Error.WriteLine(
                $"Could not find {ServiceExeName} next to nos.exe. Build the whole solution, or run " +
                $"the service executable directly with '{verb}'.");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            // ShellExecute + runas is what raises the UAC prompt; without it the child would
            // inherit this unelevated token and fail with an access-denied on the SCM.
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add(verb);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return 1;

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Console.Error.WriteLine("Elevation was declined.");
            return 1;
        }
    }

    private static string? FindServiceExecutable()
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath);
        if (directory is null)
            return null;

        var candidate = Path.Combine(directory, ServiceExeName);
        return File.Exists(candidate) ? candidate : null;
    }
}
