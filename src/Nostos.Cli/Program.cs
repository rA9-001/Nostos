using Nostos.Cli;
using Nostos.Cli.Commands;

var commandLine = CommandLine.Parse(args);
var command = commandLine.Positional.FirstOrDefault()?.ToLowerInvariant() ?? "help";

if (commandLine.Has("help") || command is "help" or "-h" or "--help")
{
    PrintUsage();
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Ctrl+C must not abandon a half-applied batch silently; the engine checks the token
    // between tweaks and the journal already holds the intent record for the one in flight.
    e.Cancel = true;
    Console.Error.WriteLine("\nCancelling after the current change...");
    cancellation.Cancel();
};

try
{
    if (command == "service")
        return await ServiceCommands.RunAsync(commandLine, cancellation.Token);

    // --service routes the whole command through the privileged daemon instead of the local
    // engine: no elevation prompt, and machine-scope tweaks work from an ordinary shell.
    if (commandLine.Has("service"))
        return await RemoteCommands.RunAsync(command, commandLine, cancellation.Token);

    var host = CliHost.Create(commandLine);

    return command switch
    {
        "list" => await CatalogCommands.ListAsync(host, commandLine, cancellation.Token),
        "categories" => await CatalogCommands.CategoriesAsync(host, commandLine, cancellation.Token),
        "status" => await CatalogCommands.StatusAsync(host, commandLine, cancellation.Token),
        "show" => await CatalogCommands.ShowAsync(host, commandLine, cancellation.Token),
        "journal" => await CatalogCommands.JournalAsync(host, commandLine, cancellation.Token),
        "doctor" => await CatalogCommands.DoctorAsync(host, commandLine, cancellation.Token),
        "apply" => await ChangeCommands.ApplyAsync(host, commandLine, cancellation.Token),
        "revert" => await ChangeCommands.RevertAsync(host, commandLine, cancellation.Token),
        "profile" => await ChangeCommands.ProfileAsync(host, commandLine, cancellation.Token),
        "bench" => await BenchCommands.RunAsync(host, commandLine, cancellation.Token),
        "update" => await UpdateCommands.RunAsync(host, commandLine, cancellation.Token),
        _ => Unknown(command),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Nostos.Ipc.ServiceUnavailableException e)
{
    Console.Error.WriteLine($"{Ansi.Red}service{Ansi.Reset}  {e.Message}");
    return 1;
}
catch (Exception e)
{
    Console.Error.WriteLine($"{Ansi.Red}error{Ansi.Reset}  {e.Message}");
    if (commandLine.Has("verbose"))
        Console.Error.WriteLine(e);
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine($"""

        {Ansi.Bold}nos{Ansi.Reset} - Windows gaming tweaks that can prove what they changed

        Every change is captured before it is made and can be undone with `nos revert`.

        {Ansi.Bold}Commands{Ansi.Reset}
          list                    Show the tweak catalog, grouped by what it improves
          categories              What the six categories are and what each one claims
          status [id...]          Show what is currently set on this machine
          show <id>               Everything about one tweak, including its options
          apply <id...>           Apply tweaks
          revert <id...> | --all  Undo tweaks, restoring the captured prior values
          bench [run|list|compare]
                                  Measure network latency, and compare two measurements
          update [--install]      Check GitHub for a newer release, and install it
          journal                 Show the change log
          doctor                  Report the environment and any pending state
          profile list | apply <path>
          service status | install | uninstall | start | stop

        {Ansi.Bold}Common options{Ansi.Reset}
          --dry-run               Show what would change without touching anything
          --yes                   Do not prompt for risky changes
          --all                   revert: undo everything this program changed
          --category <name>       list: performance, input-lag, ping, stability,
                                  interruptions, background
          --risk <level>          list: maximum risk to show (safe, moderate, risky, experimental)
          --pid <n>               Target a running process (process-scoped tweaks)
          --process <name>        Target a running process by image name
          --set key=value         Pass a parameter to a tweak (repeatable)
          --no-restore-point      Skip the System Restore point (accepts the risk)
          --service               Route the command through the privileged service (no UAC prompt)
          --verbose               Debug logging and full stack traces

        {Ansi.Bold}Examples{Ansi.Reset}
          nos list --category ping
          nos apply mmcss.system-responsiveness --dry-run
          nos show mmcss.system-responsiveness
          nos apply mmcss.system-responsiveness --set reserve=none
          nos apply graphics.hags --service      # machine-scope, no elevated shell needed
          nos revert --all

        """);
}
