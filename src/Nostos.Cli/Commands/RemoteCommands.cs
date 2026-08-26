using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.Cli.Commands;

/// <summary>
/// The same commands as the local path, routed through the service instead of the engine.
///
/// Selected with <c>--service</c>. The difference the user actually cares about: no elevation
/// prompt, because the privileged work happens in a process that is already privileged. That
/// is what makes changing a tweak mid-match possible.
/// </summary>
public static class RemoteCommands
{
    public static async Task<int> RunAsync(string command, CommandLine commandLine, CancellationToken ct)
    {
        await using var client = await OptimizerClient.ConnectAsync(ct: ct);
        await client.PingAsync(ct);

        return command switch
        {
            "list" => await ListAsync(client, commandLine, ct),
            "status" => await StatusAsync(client, commandLine, ct),
            "apply" => await ApplyAsync(client, commandLine, ct),
            "revert" => await RevertAsync(client, commandLine, ct),
            "journal" => await JournalAsync(client, commandLine, ct),
            "reconcile" => Report(await client.ReconcileAsync(ct), "re-applied"),
            "profile" => await ProfileAsync(client, commandLine, ct),
            _ => Unsupported(command),
        };
    }

    private static int Unsupported(string command)
    {
        Console.Error.WriteLine(
            $"'{command}' is not available over --service. Run it without --service to use the " +
            "engine directly.");
        return 2;
    }

    private static async Task<int> ListAsync(OptimizerClient client, CommandLine commandLine, CancellationToken ct)
    {
        var tweaks = await client.ListAsync(ct);
        var category = commandLine.Get("category");

        var shown = tweaks
            .Where(t => category is null || t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => TweakCategories.OrderOf(t.Category))
            .ThenBy(t => t.Id, StringComparer.Ordinal);

        // Grouped the same way as the local listing. The category names come from this build,
        // not from the service, so a service running an older catalog still renders sensibly.
        foreach (var group in shown.GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Bold}{TweakCategories.NameOf(group.Key).ToUpperInvariant()}{Ansi.Reset}");

            foreach (var tweak in group)
                Console.WriteLine($"  {tweak.Id,-32} {tweak.Risk,-12} {tweak.Evidence,-10} {tweak.Title}");
        }

        Console.WriteLine();
        Console.WriteLine($"{tweaks.Count} tweak(s) reported by the service.");
        return 0;
    }

    private static async Task<int> StatusAsync(OptimizerClient client, CommandLine commandLine, CancellationToken ct)
    {
        var statuses = await client.StatusAsync(BuildRequest(commandLine), ct);

        Console.WriteLine();
        foreach (var status in statuses
                     .OrderBy(s => TweakCategories.OrderOf(s.Tweak.Category))
                     .ThenBy(s => s.Tweak.Id, StringComparer.Ordinal))
        {
            var marker = status.IsApplied ? $"{Ansi.Green}on {Ansi.Reset}" : $"{Ansi.Dim}off{Ansi.Reset}";
            var managed = status.IsManagedByUs ? $" {Ansi.Cyan}[managed]{Ansi.Reset}" : "";
            var blocked = status.IsApplicable
                ? ""
                : $" {Ansi.Yellow}[n/a: {status.NotApplicableReason}]{Ansi.Reset}";

            Console.WriteLine($"{marker} {status.Tweak.Id,-34}{managed}{blocked}");
            Console.WriteLine($"    {Ansi.Dim}{status.StateDescription}{Ansi.Reset}");
        }

        return 0;
    }

    private static async Task<int> ApplyAsync(OptimizerClient client, CommandLine commandLine, CancellationToken ct)
    {
        var request = BuildRequest(commandLine);
        if (request.TweakIds.Count == 0)
        {
            Console.Error.WriteLine("usage: nos apply <tweak-id> [...] --service");
            return 2;
        }

        return Report(await client.ApplyAsync(request, ct), "applied");
    }

    private static async Task<int> RevertAsync(OptimizerClient client, CommandLine commandLine, CancellationToken ct)
    {
        var request = BuildRequest(commandLine) with { All = commandLine.Has("all") };
        if (!request.All && request.TweakIds.Count == 0)
        {
            Console.Error.WriteLine("usage: nos revert <tweak-id> [...] --service   |   nos revert --all --service");
            return 2;
        }

        var results = await client.RevertAsync(request, ct);
        if (results.Count == 0)
        {
            Console.WriteLine("Nothing to revert: the service reports no outstanding changes.");
            return 0;
        }

        return Report(results, "reverted");
    }

    private static async Task<int> JournalAsync(OptimizerClient client, CommandLine commandLine, CancellationToken ct)
    {
        var lines = await client.JournalAsync(commandLine.GetInt("tail") ?? 30, ct);

        Console.WriteLine();
        foreach (var line in lines)
        {
            Console.WriteLine(
                $"{line.TimestampUtc:yyyy-MM-dd HH:mm:ss} {line.Action,-16} {line.TweakId,-34} " +
                $"{Ansi.Dim}{line.Origin}{Ansi.Reset}");
            if (line.Detail is not null)
                Console.WriteLine($"    {Ansi.Dim}{line.Detail}{Ansi.Reset}");
            if (line.Error is not null)
                Console.WriteLine($"    {Ansi.Red}{line.Error}{Ansi.Reset}");
        }

        return 0;
    }

    private static async Task<int> ProfileAsync(OptimizerClient client, CommandLine commandLine, CancellationToken ct)
    {
        var sub = commandLine.Positional.ElementAtOrDefault(1)?.ToLowerInvariant();

        switch (sub)
        {
            case "list" or null:
            {
                var profiles = await client.ProfilesAsync(ct);
                Console.WriteLine();
                foreach (var profile in profiles)
                {
                    Console.WriteLine($"{Ansi.Bold}{profile.Name}{Ansi.Reset} - {profile.Description}");
                    Console.WriteLine($"  {profile.TweakCount} tweak(s)");
                }
                return 0;
            }

            case "apply":
            {
                var name = commandLine.Positional.ElementAtOrDefault(2);
                if (name is null)
                {
                    Console.Error.WriteLine("usage: nos profile apply <name> --service");
                    return 2;
                }

                var results = await client.ApplyProfileAsync(
                    new ApplyProfileRequest(name, commandLine.Has("dry-run")), ct);
                return Report(results, "applied");
            }

            default:
                Console.Error.WriteLine("usage: nos profile list --service | nos profile apply <name> --service");
                return 2;
        }
    }

    private static ChangeRequest BuildRequest(CommandLine commandLine) => new()
    {
        TweakIds = commandLine.Positional.Skip(1).ToList(),
        Options = commandLine.Options,
        TargetProcessId = commandLine.GetInt("pid"),
        TargetProcessName = commandLine.Get("process"),
        DryRun = commandLine.Has("dry-run"),
        Origin = commandLine.Get("origin") ?? "cli",
    };

    private static int Report(IReadOnlyList<ChangeResult> results, string verb)
    {
        Console.WriteLine();
        foreach (var result in results)
        {
            var colour = result.Outcome switch
            {
                Outcome.Applied or Outcome.Reverted => Ansi.Green,
                Outcome.AlreadyApplied or Outcome.NothingToRevert or Outcome.Skipped => Ansi.Dim,
                Outcome.Unverified or Outcome.RolledBack => Ansi.Yellow,
                _ => Ansi.Red,
            };

            var label = result.Outcome.ToString().ToLowerInvariant();
            Console.WriteLine($"{colour}{label}{Ansi.Reset}{new string(' ', Math.Max(1, 16 - label.Length))}" +
                              $"{result.TweakId,-34} {result.Message}");
        }

        var changed = results.Count(r => r.Outcome is Outcome.Applied or Outcome.Reverted or Outcome.Unverified);
        var failed = results.Count(r => r.Outcome is Outcome.Failed or Outcome.RolledBack);
        var skipped = results.Count(r => r.Outcome == Outcome.Skipped);

        Console.WriteLine();
        Console.WriteLine(
            $"{changed} {verb}, {failed} failed, {skipped} skipped, " +
            $"{results.Count - changed - failed - skipped} unchanged. {Ansi.Dim}(via service){Ansi.Reset}");

        if (results.Any(r => r.RequiresReboot && r.Outcome is Outcome.Applied or Outcome.Reverted))
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Yellow}A reboot is required for at least one change.{Ansi.Reset}");
        }

        return failed > 0 ? 1 : 0;
    }
}
