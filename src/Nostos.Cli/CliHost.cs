using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Journal;
using Nostos.Core.Safety;
using Nostos.Tweaks;
using Nostos.Win32.Services;

namespace Nostos.Cli;

/// <summary>
/// Wires the engine together for a single CLI invocation.
///
/// The CLI talks to the engine directly rather than through the service. That is intentional:
/// it keeps the engine usable and auditable on its own, and it is how the integration tests
/// drive real tweaks without installing anything.
/// </summary>
public sealed class CliHost
{
    private CliHost(TweakEngine engine, IJournal journal, ILogSink log)
    {
        Engine = engine;
        Journal = journal;
        Log = log;
    }

    public TweakEngine Engine { get; }
    public IJournal Journal { get; }
    public ILogSink Log { get; }

    public static CliHost Create(CommandLine commandLine)
    {
        AppPaths.EnsureCreated();

        var log = new ConsoleLog(commandLine.Has("verbose"));
        var journal = new JsonlJournal(AppPaths.JournalPath, log);
        var gate = new SystemRestoreSafetyGate(log)
        {
            RequireRestorePointForRiskyTweaks = !commandLine.Has("no-restore-point"),
        };

        var engine = new TweakEngine(
            CatalogFactory.CreateRegistry(), journal, WindowsPrivilegeCheck.Instance, gate, log);

        return new CliHost(engine, journal, log);
    }

    /// <summary>Builds the per-invocation context, resolving --pid / --process to a target.</summary>
    public TweakContext BuildContext(CommandLine commandLine)
    {
        int? pid = commandLine.GetInt("pid");
        string? processName = commandLine.Get("process");

        if (pid is null && processName is not null)
        {
            var matches = ProcessControl.FindByName(processName);
            if (matches.Count == 0)
                throw new InvalidOperationException($"No running process named '{processName}'.");

            // Pick the largest working set: launchers spawn several processes sharing a name,
            // and the one actually rendering is reliably the heaviest.
            var target = matches.OrderByDescending(p => p.WorkingSet64).First();
            pid = target.Id;
            processName = target.ProcessName;
            foreach (var process in matches)
                process.Dispose();
        }
        else if (pid is not null && processName is null)
        {
            processName = SafeProcessName(pid.Value);
        }

        return new TweakContext
        {
            Options = commandLine.Options,
            TargetProcessId = pid,
            TargetProcessName = processName,
            DryRun = commandLine.Has("dry-run"),
            Log = Log,
        };
    }

    private static string? SafeProcessName(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
