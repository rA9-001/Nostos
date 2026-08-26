using Nostos.Core.Updates;
using Nostos.Win32.Updates;

namespace Nostos.Cli.Commands;

/// <summary>
/// Checks GitHub for a newer release, and installs one when asked.
///
/// `nos update` checks and reports. `nos update --install` is the one that changes anything, and
/// it is a separate flag rather than a prompt so that the checking half is safe to run from a
/// script, a scheduled task or a shell that nobody is watching.
/// </summary>
public static class UpdateCommands
{
    public static async Task<int> RunAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        using var client = new UpdateClient();

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Dim}Checking {UpdateSource.Owner}/{UpdateSource.Repository}...{Ansi.Reset}");

        var status = await client.CheckAsync(ct).ConfigureAwait(false);

        if (status.Problem is not null)
        {
            Console.WriteLine($"{Ansi.Yellow}Could not check for updates: {status.Problem}{Ansi.Reset}");
            Console.WriteLine($"{Ansi.Dim}{UpdateSource.ReleasesPage}{Ansi.Reset}");
            Console.WriteLine();
            return 1;
        }

        if (status.Latest is null)
        {
            Console.WriteLine("No releases have been published yet.");
            Console.WriteLine();
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Installed   {status.Current.ToString(3)}");
        Console.WriteLine($"  Latest      {status.Latest.Version.ToString(3)}  " +
                          $"{Ansi.Dim}({status.Latest.PublishedUtc.ToLocalTime():yyyy-MM-dd}){Ansi.Reset}");
        Console.WriteLine($"  Installed as {Ansi.Dim}{UpdateInstaller.Kind}{Ansi.Reset}");

        if (!status.UpdateAvailable)
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Green}Up to date.{Ansi.Reset}");
            Console.WriteLine();
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}{status.Latest.Tag} is available.{Ansi.Reset}");
        Console.WriteLine($"{Ansi.Dim}{status.Latest.HtmlUrl}{Ansi.Reset}");

        if (status.Latest.Notes.Length > 0)
        {
            Console.WriteLine();
            foreach (var line in status.Latest.Notes.Split('\n').Take(15))
                Console.WriteLine($"  {line.TrimEnd()}");
        }

        if (!commandLine.Has("install"))
        {
            Console.WriteLine();
            Console.WriteLine("Run `nos update --install` to download and install it.");
            Console.WriteLine();
            return 0;
        }

        if (!ReleaseIntegrity.IsSigningConfigured)
        {
            Console.WriteLine();
            Console.Error.WriteLine(
                "This build has no release signing key compiled in, so it cannot verify a download.");
            Console.Error.WriteLine($"Download it by hand from {UpdateSource.ReleasesPage}");
            Console.WriteLine();
            return 1;
        }

        Console.WriteLine();
        var lastReported = -1;
        var progress = new Progress<double>(fraction =>
        {
            var percent = (int)(fraction * 100);
            if (percent / 5 == lastReported / 5)
                return;
            lastReported = percent;
            Console.Write($"\r  downloading {percent}%   ");
        });

        var outcome = await new UpdateInstaller(host.Log)
            .ApplyAsync(client, status.Latest, progress, ct)
            .ConfigureAwait(false);

        Console.Write($"\r{new string(' ', 30)}\r");

        if (!outcome.Applied)
        {
            Console.Error.WriteLine($"{Ansi.Red}{outcome.Message}{Ansi.Reset}");
            Console.WriteLine();
            return 1;
        }

        Console.WriteLine($"{Ansi.Green}{outcome.Message}{Ansi.Reset}");
        Console.WriteLine();
        return 0;
    }
}
