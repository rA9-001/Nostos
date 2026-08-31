using Nostos.Core.Localization;
using System.ComponentModel;
using System.Diagnostics;
using Nostos.Core;
using Nostos.Win32.ServiceControl;

namespace Nostos.Win32.Removal;

/// <summary>What removing this copy would touch on this machine.</summary>
/// <param name="ServiceInstalled">True when the LocalSystem service is registered.</param>
/// <param name="DataRoot">Journal, logs, profiles and settings.</param>
/// <param name="IsPortable">True when <paramref name="DataRoot"/> sits inside the app's own folder.</param>
/// <param name="LocalState">The per-user renderer cache, or null when this build never made one.</param>
/// <param name="InstallDirectory">The folder this copy runs from.</param>
/// <param name="ExecutablePath">The running executable.</param>
/// <param name="SingleFile">True when this copy is one self-contained exe with no service beside it.</param>
/// <param name="Helper">The elevated helper, or null when this copy does not ship one.</param>
public sealed record RemovalTargets(
    bool ServiceInstalled,
    string DataRoot,
    bool IsPortable,
    string? LocalState,
    string InstallDirectory,
    string ExecutablePath,
    bool SingleFile,
    string? Helper)
{
    /// <summary>
    /// What the user has to delete themselves, once everything else is gone.
    ///
    /// Not simply the install directory. A single-file copy is one executable that people run
    /// straight out of Downloads, and telling somebody to delete the folder it is sitting in
    /// would be telling them to delete their Downloads folder. So a one-file copy names the
    /// executable and its data folder, and a folder install names the folder.
    /// </summary>
    public IReadOnlyList<string> DeleteByHand => SingleFile
        ? IsPortable ? [ExecutablePath, DataRoot] : [ExecutablePath]
        : [InstallDirectory];

    /// <summary>
    /// Whether an administrator prompt is unavoidable.
    ///
    /// Only the service makes it certain: deleting a service registration is an SCM call no
    /// ordinary user can make. Files are a maybe -- the data folder is usually owned by the
    /// user who first ran the app, and is usually deletable without a prompt -- so removal
    /// tries unelevated first and escalates only if something refuses to go.
    /// </summary>
    public bool NeedsElevation => ServiceInstalled;
}

/// <param name="Completed">True when nothing this program put on the machine is left outside the app's own folder.</param>
/// <param name="Done">What was removed, in the order it happened, for the user to read.</param>
/// <param name="Leftovers">Paths that would not go. Empty on a clean removal.</param>
/// <param name="DeleteByHand">What is left for the user to delete, filtered to what still exists.</param>
/// <param name="Problem">Why it stopped short, or null.</param>
public sealed record RemovalResult(
    bool Completed,
    IReadOnlyList<string> Done,
    IReadOnlyList<string> Leftovers,
    IReadOnlyList<string> DeleteByHand,
    string? Problem = null);

/// <summary>
/// Takes Nostos back off the machine.
///
/// The promise this implements is narrow and worth stating exactly: after it runs, deleting the
/// folder the app was run from leaves a PC with no trace of this program on it. That means the
/// service registration, the data folder under %ProgramData%, and the per-user renderer cache --
/// everything the app writes that is not inside its own directory.
///
/// <para>Two things it deliberately does not do.</para>
///
/// <para>It does not revert tweaks. That happens first, through the ordinary revert path, driven
/// by whoever called this, because reverting is a change to the machine like any other and it
/// belongs in the journal that is about to be deleted -- not in a special uninstall codepath
/// that nothing else exercises.</para>
///
/// <para>It does not touch System Restore points. Windows made those, they protect everything
/// else on the PC as well, and deleting somebody's recovery history to tidy up after ourselves
/// would be a far larger act than removing an app.</para>
/// </summary>
public static class SystemRemoval
{
    /// <summary>Argument the elevated half is launched with. See <c>Nostos.Service.exe remove</c>.</summary>
    public const string HelperVerb = "remove";

    /// <summary>Reads what is on this machine. Cheap, unelevated, and never throws.</summary>
    /// <param name="helper">
    /// Path to <c>Nostos.Service.exe</c>, when the caller knows where it is. The app does: it
    /// already resolves it for the service setup, including the development-tree fallback.
    /// </param>
    public static RemovalTargets Inspect(string? helper = null)
    {
        var installDirectory = Path.GetDirectoryName(Environment.ProcessPath!) ?? AppContext.BaseDirectory;

        helper ??= Path.Combine(installDirectory, "Nostos.Service.exe");
        if (!File.Exists(helper))
            helper = null;

        var localState = Directory.Exists(AppPaths.LocalStateRoot) ? AppPaths.LocalStateRoot : null;

        var serviceInstalled = false;
        try
        {
            serviceInstalled = ServiceInstaller.IsInstalled();
        }
        catch (Exception e) when (e is Win32Exception or UnauthorizedAccessException)
        {
            // QueryState is documented not to throw, and it does not. This is here because a
            // panel that tells the user what is about to be removed must not be the thing that
            // crashes the window.
        }

        // The same rule the updater uses to tell the two shapes of install apart: a folder
        // build has the service beside the app, a one-file build does not.
        var singleFile = !File.Exists(Path.Combine(installDirectory, "Nostos.Service.exe"));

        return new RemovalTargets(
            serviceInstalled,
            AppPaths.Root,
            AppPaths.IsPortable,
            localState,
            installDirectory,
            Environment.ProcessPath ?? Path.Combine(installDirectory, "Nostos.exe"),
            singleFile,
            helper);
    }

    /// <summary>
    /// Removes everything outside the app's own folder.
    ///
    /// Call it after reverting; the journal is deleted here, and a machine left with applied
    /// tweaks and no journal is one where nothing can prove what was changed.
    /// </summary>
    public static async Task<RemovalResult> RunAsync(string? helper = null, CancellationToken ct = default)
    {
        var targets = Inspect(helper);
        var done = new List<string>();
        var leftovers = new List<string>();

        if (targets.NeedsElevation)
        {
            var problem = await RunHelperAsync(targets.Helper, ct).ConfigureAwait(false);
            if (problem is not null)
                return new RemovalResult(false, done, leftovers, Remaining(targets), problem);

            // Windows only removes a service registration once every handle to it is closed --
            // an open services.msc is enough to defer it. The service is stopped and will never
            // run again either way, but saying "removed" while the key is still there would be
            // a claim the user could go and disprove.
            done.Add(ServiceInstaller.IsInstalled()
                ? Strings.Get("removal.step.servicestopped")
                : Strings.Get("removal.step.serviceremoved"));
        }

        // Whatever the helper did or did not do, the answer to "is it gone" is read off the
        // disk rather than assumed from an exit code.
        if (Directory.Exists(targets.DataRoot))
        {
            DeleteTree(targets.DataRoot, leftovers);

            // A folder the user owns usually goes without a prompt. One written by the service
            // running as LocalSystem does not, and that is worth one more elevation rather than
            // leaving a journal of everything that was ever changed sitting on the machine.
            if (leftovers.Count > 0 && !targets.IsPortable && targets.Helper is not null && !targets.NeedsElevation)
            {
                leftovers.Clear();
                if (await RunHelperAsync(targets.Helper, ct).ConfigureAwait(false) is null)
                    DeleteTree(targets.DataRoot, leftovers);
            }
        }

        if (!Directory.Exists(targets.DataRoot))
            done.Add(Strings.Format("removal.step.deleted", targets.DataRoot));

        if (targets.LocalState is { } cache && Directory.Exists(cache))
        {
            DeleteTree(cache, leftovers);
            if (!Directory.Exists(cache))
                done.Add(Strings.Format("removal.step.deleted", cache));
        }

        // Leftovers inside the app's own folder do not count against the promise: the user is
        // being told to delete that folder, and it takes them with it. This is the ordinary
        // case in a portable copy, whose renderer is loaded from data\runtime as it runs.
        var outside = leftovers
            .Where(path => !IsInside(path, targets.InstallDirectory))
            .ToList();

        return new RemovalResult(outside.Count == 0, done, outside, Remaining(targets));
    }

    /// <summary>
    /// The paths the user still has to delete, read off the disk rather than predicted.
    ///
    /// A portable copy's data folder usually survives -- the renderer is loaded out of it while
    /// the app is running -- and an unlucky one may not. Listing a path that is already gone
    /// would send somebody looking for a folder that is not there and leave them wondering what
    /// else this program is wrong about.
    /// </summary>
    private static IReadOnlyList<string> Remaining(RemovalTargets targets)
        => targets.DeleteByHand
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();

    /// <summary>Runs the elevated half and waits for it. Returns null on success.</summary>
    private static async Task<string?> RunHelperAsync(string? helper, CancellationToken ct)
    {
        if (helper is null)
        {
            return Strings.Get("removal.problem.servicenothere");
        }

        var start = new ProcessStartInfo
        {
            FileName = helper,
            // The app is asInvoker and stays that way. Deleting a service registration needs an
            // elevated token, so a child gets one; this process never does.
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add(HelperVerb);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return Strings.Get("removal.problem.helperstart");

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return process.ExitCode == 0
                ? null
                : Strings.Format("removal.problem.helperfailed", process.ExitCode);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the prompt was declined. A decision, not a fault.
            return Strings.Get("removal.problem.uacdeclined");
        }
        catch (Exception e)
        {
            return Strings.Format("removal.problem.helperrun", e.Message);
        }
    }

    /// <summary>
    /// Deletes a tree, and reports what would not go rather than throwing.
    ///
    /// The recursive delete is attempted first because it is one syscall for the whole tree.
    /// When it fails -- one locked file is enough -- the walk that follows removes everything
    /// that *can* go and names the rest, so the caller can tell the user which files are still
    /// there instead of only that something went wrong.
    /// </summary>
    public static void DeleteTree(string root, List<string> leftovers)
    {
        try
        {
            Directory.Delete(root, recursive: true);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        foreach (var file in SafeEnumerate(root))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                leftovers.Add(file);
            }
        }

        try
        {
            // Deletes the directories that are now empty. Anything still holding a file stays,
            // and has already been named above.
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (leftovers.Count == 0)
                leftovers.Add(root);
        }
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Whether a path sits inside a directory, comparing resolved full paths.</summary>
    public static bool IsInside(string path, string directory)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException)
        {
            return false;
        }
    }
}
