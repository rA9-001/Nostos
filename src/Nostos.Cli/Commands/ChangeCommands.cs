using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Profiles;

namespace Nostos.Cli.Commands;

/// <summary>Commands that change the machine. Every one of them is undoable by `nos revert`.</summary>
public static class ChangeCommands
{
    public static async Task<int> ApplyAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var ids = commandLine.Positional.Skip(1).ToList();
        if (ids.Count == 0)
        {
            Console.Error.WriteLine("usage: nos apply <tweak-id> [<tweak-id>...] [--set key=value] [--dry-run]");
            return 2;
        }

        var context = host.BuildContext(commandLine);
        var selections = ids.Select(id => new TweakSelection(id, commandLine.Options)).ToList();

        if (!await ConfirmRiskyAsync(host, selections, commandLine, ct).ConfigureAwait(false))
            return 1;

        var results = await host.Engine
            .ApplyManyAsync(selections, context, commandLine.Get("origin") ?? "manual", ct)
            .ConfigureAwait(false);

        return Report(results, "applied");
    }

    public static async Task<int> RevertAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var context = host.BuildContext(commandLine);

        if (commandLine.Has("all"))
        {
            var all = await host.Engine.RevertAllAsync(context, "manual", ct).ConfigureAwait(false);
            if (all.Count == 0)
            {
                Console.WriteLine("Nothing to revert: this machine has no outstanding changes.");
                return 0;
            }

            return Report(all, "reverted");
        }

        var ids = commandLine.Positional.Skip(1).ToList();
        if (ids.Count == 0)
        {
            Console.Error.WriteLine("usage: nos revert <tweak-id> [<tweak-id>...]   |   nos revert --all");
            return 2;
        }

        var results = new List<TweakOperationResult>();
        foreach (var id in ids)
            results.Add(await host.Engine.RevertAsync(id, context, "manual", ct).ConfigureAwait(false));

        return Report(results, "reverted");
    }

    public static async Task<int> ProfileAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var sub = commandLine.Positional.ElementAtOrDefault(1);
        switch (sub)
        {
            case "list":
            {
                var profiles = ProfileLoader.LoadDirectory(AppPaths.ProfilesDirectory);
                if (profiles.Count == 0)
                {
                    Console.WriteLine($"No profiles in {AppPaths.ProfilesDirectory}.");
                    return 0;
                }

                foreach (var profile in profiles)
                {
                    Console.WriteLine($"{Ansi.Bold}{profile.Name}{Ansi.Reset} - {profile.Description}");
                    Console.WriteLine($"  {profile.Tweaks.Count} tweak(s)");
                }
                return 0;
            }

            case "apply":
            {
                var path = commandLine.Positional.ElementAtOrDefault(2);
                if (path is null)
                {
                    Console.Error.WriteLine("usage: nos profile apply <path-to-profile.json>");
                    return 2;
                }

                var profile = ProfileLoader.Load(path);
                var context = host.BuildContext(commandLine);

                var selections = profile.Tweaks.ToList();

                if (!await ConfirmRiskyAsync(host, selections, commandLine, ct).ConfigureAwait(false))
                    return 1;

                var results = await host.Engine
                    .ApplyManyAsync(selections, context, $"profile:{profile.Name}", ct)
                    .ConfigureAwait(false);

                return Report(results, "applied");
            }

            default:
                Console.Error.WriteLine("usage: nos profile list   |   nos profile apply <path>");
                return 2;
        }
    }

    /// <summary>
    /// Requires an explicit yes for anything risky or reboot-requiring, unless --yes was passed.
    /// Non-interactive sessions must pass --yes rather than being auto-approved.
    /// </summary>
    private static Task<bool> ConfirmRiskyAsync(
        CliHost host, IReadOnlyList<TweakSelection> selections, CommandLine commandLine, CancellationToken ct)
    {
        if (commandLine.Has("yes") || commandLine.Has("dry-run"))
            return Task.FromResult(true);

        var risky = selections
            .Select(s => host.Engine.Registry.Find(s.TweakId)?.Metadata)
            .OfType<TweakMetadata>()
            .Where(m => m.Risk >= Risk.Risky || m.RequiresReboot)
            .ToList();

        if (risky.Count == 0)
            return Task.FromResult(true);

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Yellow}The following changes are risky or need a reboot:{Ansi.Reset}");
        foreach (var meta in risky)
        {
            Console.WriteLine($"  {meta.Id}  ({Ansi.Yellow}{meta.Risk.ToString().ToLowerInvariant()}{Ansi.Reset}" +
                              $"{(meta.RequiresReboot ? ", needs reboot" : "")})");
            Console.WriteLine($"    {Ansi.Dim}{meta.Summary}{Ansi.Reset}");
        }

        Console.WriteLine();
        Console.WriteLine("A System Restore point will be taken first. Nothing else undoes these:");
        Console.WriteLine("if one turns out to be wrong, `nos revert <id>` puts it back.");
        Console.Write("Continue? [y/N] ");

        if (Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Error.WriteLine("Refusing to assume yes in a non-interactive session. Pass --yes.");
            return Task.FromResult(false);
        }

        var answer = Console.ReadLine();
        return Task.FromResult(answer is not null && answer.Trim()
            .StartsWith("y", StringComparison.OrdinalIgnoreCase));
    }

    private static int Report(IReadOnlyList<TweakOperationResult> results, string verb)
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

        var changed = results.Count(r => r.ChangedSomething);
        var failed = results.Count(r => !r.IsSuccess);
        var skipped = results.Count(r => r.Outcome == Outcome.Skipped);

        Console.WriteLine();
        Console.WriteLine(
            $"{changed} {verb}, {failed} failed, {skipped} skipped, " +
            $"{results.Count - changed - failed - skipped} unchanged.");

        if (results.Any(r => r.RequiresReboot && r.ChangedSomething))
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Yellow}A reboot is required for at least one change to take effect.{Ansi.Reset}");
        }

        return failed > 0 ? 1 : 0;
    }
}
