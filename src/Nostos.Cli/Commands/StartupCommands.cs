using Nostos.Core.Journal;
using Nostos.Win32.Services;

namespace Nostos.Cli.Commands;

/// <summary>
/// The startup list, from a terminal.
///
/// Everything the window can do here, the CLI can do too. That is not symmetry for its own sake:
/// the startup list is exactly the thing somebody wants to set from a provisioning script after
/// reinstalling Windows, and a feature that exists only behind a mouse is one that has to be
/// re-done by hand on every machine.
/// </summary>
public static class StartupCommands
{
    public static async Task<int> RunAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
        => commandLine.Positional.ElementAtOrDefault(1)?.ToLowerInvariant() switch
        {
            null or "list" => List(),
            "enable" => await SetAsync(host, commandLine, enabled: true, ct).ConfigureAwait(false),
            "disable" => await SetAsync(host, commandLine, enabled: false, ct).ConfigureAwait(false),
            var other => Unknown(other),
        };

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"Unknown startup command '{verb}'. Try list, enable or disable.");
        return 2;
    }

    private static int List()
    {
        var items = StartupItems.List();
        if (items.Count == 0)
        {
            Console.WriteLine("Nothing runs at sign-in on this PC.");
            return 0;
        }

        var idWidth = items.Max(i => i.Id.Length);

        Console.WriteLine();
        Console.WriteLine(
            $"{Ansi.Bold}{items.Count(i => i.IsEnabled)} of {items.Count} enabled{Ansi.Reset}");
        Console.WriteLine();

        foreach (var item in items)
        {
            var state = item.IsEnabled
                ? $"{Ansi.Green}on {Ansi.Reset}"
                : $"{Ansi.Dim}off{Ansi.Reset}";

            Console.WriteLine($"  {state} {item.Id.PadRight(idWidth)}  {item.Name}");
            Console.WriteLine(
                $"      {Ansi.Dim}{item.ExecutablePath ?? item.Command}{Ansi.Reset}");
        }

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Dim}  nos startup disable <id>{Ansi.Reset}");
        return 0;
    }

    private static async Task<int> SetAsync(
        CliHost host, CommandLine commandLine, bool enabled, CancellationToken ct)
    {
        if (commandLine.Positional.ElementAtOrDefault(2) is not { Length: > 0 } id)
        {
            Console.Error.WriteLine("Which one? Run `nos startup list` for the ids.");
            return 2;
        }

        if (StartupWire.Find(id) is not { } item)
        {
            Console.Error.WriteLine(
                $"No startup entry with id '{id}'. Run `nos startup list` to see them.");
            return 2;
        }

        try
        {
            StartupItems.SetEnabled(item, enabled);
        }
        catch (UnauthorizedAccessException)
        {
            // The one failure worth explaining rather than reporting. A machine-wide entry lives
            // in HKLM, and the fix is a word the reader can act on rather than a stack trace.
            Console.Error.WriteLine(
                $"'{item.Name}' applies to every account on this PC, so switching it needs "
                + "administrator rights. Run this from an elevated terminal.");
            return 1;
        }

        // The same line the window and the service write, so the History tab tells one story
        // wherever the switch was flicked.
        await StartupJournal.RecordAsync(host.Journal, item.Id, item.Name, enabled, ct)
            .ConfigureAwait(false);

        // Read it back rather than reporting what was asked for.
        var after = StartupWire.Find(id);
        var state = after?.IsEnabled == true ? "on" : "off";

        Console.WriteLine($"{item.Name} is now {state}.");
        return after?.IsEnabled == enabled ? 0 : 1;
    }
}
