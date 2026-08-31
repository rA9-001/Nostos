using System.Diagnostics;

namespace Nostos.App.ViewModels;

/// <summary>
/// One process the user could point a process-scoped tweak at.
/// </summary>
/// <param name="Id">The pid, which is what actually gets sent.</param>
/// <param name="Name">The executable name without its extension, e.g. <c>helldivers2</c>.</param>
/// <param name="Title">Its main window's title, or "" when it has no window.</param>
public sealed record RunningProcess(int Id, string Name, string Title)
{
    /// <summary>
    /// What the picker shows: the window title if there is one, then the executable and pid.
    ///
    /// Both halves earn their place. The title is how somebody recognises the game they are
    /// looking at; the executable and pid are how they tell two windows of the same program
    /// apart, and what the journal will show afterwards.
    /// </summary>
    public string Display => Title.Length > 0
        ? $"{Title}  —  {Name}.exe ({Id})"
        : $"{Name}.exe ({Id})";
}

/// <summary>
/// Lists the processes worth offering as a target.
///
/// Only processes with a visible main window. A machine has two or three hundred processes and
/// almost all of them are services, workers and shell infrastructure that nobody wants to raise
/// the priority of; a game is a window. This does mean a game running under a launcher that
/// hides its window would not appear, which is why the CLI keeps taking <c>--pid</c> for
/// anything this list does not show.
///
/// Nothing here reads another process's memory or opens it for anything but the identity
/// Windows already publishes: the name, the pid and the window title. That is a hard line for
/// this program -- see docs/architecture.md -- and a process picker is exactly the kind of
/// feature that would quietly cross it if it started asking better questions.
/// </summary>
internal static class RunningProcesses
{
    public static IReadOnlyList<RunningProcess> List()
    {
        var found = new List<RunningProcess>();
        var self = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // Not ourselves. Raising this window's own priority is not a thing anybody
                // means to do, and offering it invites the one misclick that has no upside.
                if (process.Id == self)
                    continue;

                // A process can exit between being enumerated and being asked anything, and a
                // protected one refuses regardless. Either way it is not a target, and neither
                // is worth an error: skip it and carry on.
                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                found.Add(new RunningProcess(process.Id, process.ProcessName, title.Trim()));
            }
            catch (Exception e) when (e is InvalidOperationException or SystemException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        // By title, so the list is stable between refreshes and can be scanned alphabetically.
        // Pid order is arrival order, which changes every time anything restarts.
        return [.. found.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(p => p.Id)];
    }
}
