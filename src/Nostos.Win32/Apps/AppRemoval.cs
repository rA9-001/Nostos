using System.Diagnostics;
using Nostos.Core;

namespace Nostos.Win32.Apps;

/// <summary>Where a removal has got to, reported as it happens so the window can show it.</summary>
/// <param name="Stage">Which of the four stages is running.</param>
/// <param name="Detail">The thing being worked on right now, already in words a reader wants.</param>
public sealed record RemovalProgress(RemovalStage Stage, string Detail);

public enum RemovalStage
{
    /// <summary>Running the program's own uninstaller, or the package manager's removal.</summary>
    Uninstalling,

    /// <summary>Looking for what it left behind.</summary>
    Scanning,

    /// <summary>Moving leftovers to the Recycle Bin and backing up registry keys.</summary>
    Sweeping,

    Finished,
}

/// <summary>What a removal actually did, for the window and for the journal.</summary>
/// <param name="Left">
/// Leftovers that are still there. Never silently empty: a sweep that could not move something
/// says so, and the row stays on screen rather than the tab claiming a clean removal it did not
/// achieve.
/// </param>
public sealed record RemovalReport(
    string AppId,
    string Name,
    bool UninstallerRan,
    bool NeededTheUser,
    int FoldersRecycled,
    int KeysRemoved,
    long BytesReclaimed,
    IReadOnlyList<Leftover> Left,
    IReadOnlyList<string> Problems)
{
    public bool IsClean => Left.Count == 0 && Problems.Count == 0;
}

/// <summary>
/// Removes a program, and then removes what its uninstaller left behind.
///
/// Two halves, and they are very different in character. The first runs the program's own
/// uninstaller -- the same thing Windows runs, doing whatever that installer's author decided.
/// The second is this program's own work: find the folders and registry keys still bearing the
/// program's name, back the keys up, and move everything to the Recycle Bin.
///
/// <para><b>Nothing here deletes.</b> Folders go to the Recycle Bin, and every registry key is
/// exported to a .reg file beside the journal before it is removed, so `reg import` puts it
/// back. That is what makes an automatic sweep defensible: the matching that finds leftovers is
/// guesswork, and guesswork is only acceptable when being wrong is survivable.</para>
///
/// <para>This program never elevates itself. An uninstaller that needs administrator rights
/// raises its own prompt, exactly as it would if it had been started from Programs and
/// Features.</para>
/// </summary>
public static class AppRemoval
{
    /// <summary>Where exported registry keys are kept, so a sweep can be undone.</summary>
    public static string BackupDirectory => Path.Combine(AppPaths.Root, "removed");

    /// <summary>
    /// How long to wait for an uninstaller before giving up on it.
    ///
    /// Generous, because some genuinely take minutes, and finite because an uninstaller that
    /// silently opened a dialog on another desktop would otherwise hang the window forever.
    /// </summary>
    public static TimeSpan UninstallTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Uninstalls, then sweeps.
    ///
    /// <paramref name="sweep"/> false stops after the uninstaller and reports what was found
    /// without touching it, which is what a dry run is for.
    /// </summary>
    public static async Task<RemovalReport> RemoveAsync(
        InstalledApp app,
        bool sweep = true,
        Func<RemovalProgress, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        var problems = new List<string>();

        await Report(onProgress, RemovalStage.Uninstalling, app.Name).ConfigureAwait(false);

        var (ran, neededUser, uninstallProblem) = await RunUninstallerAsync(app, ct).ConfigureAwait(false);
        if (uninstallProblem is not null)
            problems.Add(uninstallProblem);

        await Report(onProgress, RemovalStage.Scanning, app.Name).ConfigureAwait(false);

        var found = Leftovers.Scan(app);

        // The most important check in this file, and the one the first draft did not have.
        //
        // A Steam game's uninstaller is `steam.exe steam://uninstall/960090`. That process hands
        // the request to the already-running Steam and exits in about a second with code zero,
        // long before Steam has even drawn the dialog asking whether you meant it. A sweep that
        // trusted the exit code would then move 118 GB of Elden Ring to the Recycle Bin while
        // the user was still deciding -- and would do the same after they pressed Cancel. Epic
        // and several launchers hand off the same way.
        //
        // So the exit code is not the evidence. The machine is: if the program is still
        // registered, it is still installed, and there is nothing here to sweep up.
        if (sweep && StillInstalled(app))
        {
            await Report(onProgress, RemovalStage.Finished, app.Name).ConfigureAwait(false);

            problems.Add(
                "still listed as installed afterwards, so nothing was swept up. Some launchers "
                + "hand the uninstall to a program that is already running and return "
                + "immediately -- finish it there, then remove it here again.");

            return new RemovalReport(app.Id, app.Name, ran, neededUser, 0, 0, 0, found, problems);
        }

        if (!sweep)
        {
            await Report(onProgress, RemovalStage.Finished, app.Name).ConfigureAwait(false);
            return new RemovalReport(app.Id, app.Name, ran, neededUser, 0, 0, 0, found, problems);
        }

        var folders = 0;
        var keys = 0;
        long bytes = 0;
        var left = new List<Leftover>();

        foreach (var leftover in found)
        {
            ct.ThrowIfCancellationRequested();
            await Report(onProgress, RemovalStage.Sweeping, leftover.Path).ConfigureAwait(false);

            if (leftover.Kind == LeftoverKind.Folder)
            {
                var result = RecycleBin.Send(leftover.Path);
                if (result.Recycled)
                {
                    folders++;
                    bytes += leftover.SizeBytes;
                }
                else
                {
                    left.Add(leftover);
                    problems.Add($"{leftover.Path}: {result.Message}");
                }

                continue;
            }

            var removed = RemoveKey(leftover.Path, app);
            if (removed is null)
            {
                keys++;
            }
            else
            {
                left.Add(leftover);
                problems.Add($"{leftover.Path}: {removed}");
            }
        }

        await Report(onProgress, RemovalStage.Finished, app.Name).ConfigureAwait(false);

        return new RemovalReport(app.Id, app.Name, ran, neededUser, folders, keys, bytes, left, problems);
    }

    /// <summary>
    /// Removes one thing belonging to a program, and nothing else.
    ///
    /// For the reader who wants the stale registry key gone but not the folder, or the orphaned
    /// icon file but not the entry. "All of it" is the common case and has its own button; this
    /// is for when it is not.
    ///
    /// <para><b>The path is checked against what this program actually owns before anything
    /// happens.</b> That check is the security boundary and not a nicety: without it a caller
    /// who can name an app id could hand over any path on the machine and have it recycled, or
    /// any registry key and have it exported and deleted. The list of removable things is
    /// rebuilt from the live machine and the request has to match one of them, compared the way
    /// each kind is compared -- registry keys allowing for both spellings of a hive.</para>
    /// </summary>
    public static async Task<RemovalReport> RemoveItemAsync(
        InstalledApp app,
        string path,
        Func<RemovalProgress, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        if (Owned(app, path) is not { } item)
        {
            return new RemovalReport(
                app.Id, app.Name, false, false, 0, 0, 0, [],
                ["that is not one of the things this entry owns, so nothing was touched"]);
        }

        ct.ThrowIfCancellationRequested();
        await Report(onProgress, RemovalStage.Sweeping, item.Path).ConfigureAwait(false);

        if (item.Kind == RemovableKind.RegistryKey)
        {
            var problem = RemoveKey(item.Path, app);
            await Report(onProgress, RemovalStage.Finished, app.Name).ConfigureAwait(false);

            return new RemovalReport(
                app.Id, app.Name, false, false, 0, problem is null ? 1 : 0, 0,
                [], problem is null ? [] : [$"{item.Path}: {problem}"]);
        }

        var result = RecycleBin.Send(item.Path);
        await Report(onProgress, RemovalStage.Finished, app.Name).ConfigureAwait(false);

        return new RemovalReport(
            app.Id, app.Name, false, false,
            result.Recycled ? 1 : 0, 0,
            result.Recycled ? item.SizeBytes : 0,
            [], result.Recycled ? [] : [$"{item.Path}: {result.Message}"]);
    }

    /// <summary>
    /// The removable thing at this path, or null when the program does not own one.
    ///
    /// Read from the machine rather than from anything the caller supplied, which is the point:
    /// the answer has to come from the same place the panel's list came from.
    /// </summary>
    public static RemovableItem? Owned(InstalledApp app, string path)
        => AppDetails.For(app).Removable.FirstOrDefault(i => i.Kind switch
        {
            RemovableKind.RegistryKey => IsSameKey(i.Path, path),
            _ => string.Equals(
                i.Path.Replace('/', '\\').TrimEnd('\\'),
                path.Replace('/', '\\').TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase),
        });

    /// <summary>
    /// Removes the registration itself, and everything found under the program's name, without
    /// running an uninstaller.
    ///
    /// For the row that outlived its program. An uninstaller that removes a program's files and
    /// leaves its Uninstall key behind produces an entry Windows still believes in and that
    /// nothing can act on: <see cref="RemoveAsync"/> would run an uninstaller that is not there,
    /// or one that shrugs because its program is already gone, and then refuse to sweep because
    /// the entry is still registered. That is the correct behaviour when the program is real and
    /// a dead end when it is not.
    ///
    /// <para>This is the same machinery pointed at the registration: the key is exported to a
    /// .reg file before it is removed, folders go to the Recycle Bin, and a failure is never
    /// turned into a delete. <c>reg import</c> puts the entry back, and the row returns.</para>
    ///
    /// <para>Refused outright for anything that is not one of the three Uninstall keys. A Store
    /// package or a scoop directory is not a note about a program, it is the program, and
    /// forgetting it would hide something that is still installed.</para>
    /// </summary>
    public static async Task<RemovalReport> ForgetAsync(
        InstalledApp app,
        Func<RemovalProgress, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        if (!AppDetails.CanForget(app))
        {
            return new RemovalReport(
                app.Id, app.Name, false, false, 0, 0, 0, [],
                ["this is not a registry entry, so there is no registration to remove on its own"]);
        }

        var problems = new List<string>();

        await Report(onProgress, RemovalStage.Scanning, app.Name).ConfigureAwait(false);

        // The registration is filtered out of the sweep because the leftover scan finds it too:
        // it is a registry key under the program's name, which is exactly what that scan looks
        // for. Removing it twice would work and then report a failure for the second attempt --
        // a row saying something went wrong when the machine is in precisely the intended state.
        var registration = AppDetails.Registration(app);
        var found = Leftovers.Scan(app).Where(l => !IsSameKey(l.Path, registration)).ToList();

        var folders = 0;
        var keys = 0;
        long bytes = 0;
        var left = new List<Leftover>();

        foreach (var leftover in found)
        {
            ct.ThrowIfCancellationRequested();
            await Report(onProgress, RemovalStage.Sweeping, leftover.Path).ConfigureAwait(false);

            if (leftover.Kind == LeftoverKind.Folder)
            {
                var result = RecycleBin.Send(leftover.Path);
                if (result.Recycled)
                {
                    folders++;
                    bytes += leftover.SizeBytes;
                }
                else
                {
                    left.Add(leftover);
                    problems.Add($"{leftover.Path}: {result.Message}");
                }

                continue;
            }

            if (RemoveKey(leftover.Path, app) is { } keyProblem)
            {
                left.Add(leftover);
                problems.Add($"{leftover.Path}: {keyProblem}");
            }
            else
            {
                keys++;
            }
        }

        // The registration goes last, so a sweep that fails half way leaves the row in the list
        // with the rest of it still findable. Removing the entry first would hide whatever was
        // left behind, which is the one outcome worse than doing nothing.
        await Report(onProgress, RemovalStage.Sweeping, registration).ConfigureAwait(false);

        if (RemoveKey(registration, app) is { } problem)
            problems.Add($"{registration}: {problem}");
        else
            keys++;

        await Report(onProgress, RemovalStage.Finished, app.Name).ConfigureAwait(false);

        return new RemovalReport(app.Id, app.Name, false, false, folders, keys, bytes, left, problems);
    }

    /// <summary>
    /// Whether two registry paths name the same key, allowing for the two ways a hive is spelled.
    ///
    /// The leftover scan writes HKCU and the registration is written HKEY_CURRENT_USER, because
    /// each is the natural form where it is produced and reg.exe accepts both. Comparing them as
    /// plain strings would quietly answer no.
    /// </summary>
    public static bool IsSameKey(string left, string right)
        => string.Equals(Expand(left), Expand(right), StringComparison.OrdinalIgnoreCase);

    private static string Expand(string path)
    {
        var text = path.Trim().TrimEnd('\\');

        foreach (var (shortForm, longForm) in Hives)
        {
            if (text.StartsWith(shortForm + "\\", StringComparison.OrdinalIgnoreCase))
                return longForm + text[shortForm.Length..];
        }

        return text;
    }

    private static readonly (string Short, string Long)[] Hives =
    [
        ("HKCU", "HKEY_CURRENT_USER"),
        ("HKLM", "HKEY_LOCAL_MACHINE"),
        ("HKCR", "HKEY_CLASSES_ROOT"),
        ("HKU", "HKEY_USERS"),
    ];

    private static Task Report(Func<RemovalProgress, Task>? onProgress, RemovalStage stage, string detail)
        => onProgress?.Invoke(new RemovalProgress(stage, detail)) ?? Task.CompletedTask;

    /// <summary>
    /// Is this program still registered on the machine?
    ///
    /// Asked afresh rather than from the list the window is holding, because the whole question
    /// is whether the machine changed underneath it. A program that is still here after its own
    /// uninstaller returned did not get uninstalled, whatever the exit code said.
    /// </summary>
    public static bool StillInstalled(InstalledApp app) => InstalledApps.Find(app.Id) is not null;

    // ------------------------------------------------------------------ the uninstaller

    /// <summary>
    /// Runs whatever removes this program, and says whether it could be done unattended.
    ///
    /// "Needed the user" is not a failure. Plenty of installers ship no silent switch at all, and
    /// the honest thing is to run their uninstaller, let it ask its questions, and say on the row
    /// that it did -- rather than guessing at command-line flags for somebody else's setup.exe
    /// and hoping. A wrong guess there runs an unattended install, not an uninstall.
    /// </summary>
    internal static async Task<(bool Ran, bool NeededUser, string? Problem)> RunUninstallerAsync(
        InstalledApp app, CancellationToken ct)
    {
        var (file, arguments, silent) = CommandFor(app);

        if (file is null)
            return (false, false, "this program registered no uninstaller");

        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath(),
            });

            if (process is null)
                return (false, false, "the uninstaller would not start");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(UninstallTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            // 1602 is "user cancelled" and 3010 is "restart required". Neither is an error, and
            // reporting them as one would put a red row next to a removal that worked.
            return process.ExitCode switch
            {
                0 or 3010 => (true, !silent, null),
                1602 => (false, true, "cancelled in the uninstaller"),
                var code => (true, !silent, $"the uninstaller exited with code {code}"),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (true, true, "the uninstaller is still running; it was left alone");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (false, false, $"could not start the uninstaller: {e.Message}");
        }
    }

    /// <summary>
    /// What to run, and whether it will run unattended.
    ///
    /// The MSI case is worth the special handling: `MsiExec.exe /I{GUID}` can be rewritten to
    /// `/X{GUID} /qn /norestart` and removed with no questions asked, even when the installer's
    /// author never supplied a QuietUninstallString. That is documented Windows Installer
    /// behaviour rather than a guess, which is exactly the line between this and inventing flags.
    /// </summary>
    public static (string? File, string Arguments, bool Silent) CommandFor(InstalledApp app)
    {
        switch (app.Source)
        {
            case AppSource.Store:
                // Through PowerShell because removing an MSIX package is a WinRT call, and this
                // program is compiled ahead of time with no WinRT projection. The cmdlet is the
                // documented path and is present on every machine that can have a package.
                var package = app.Id.Split(':', 2)[^1];
                return ("powershell.exe",
                    $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{package}'\"", true);

            case AppSource.Scoop:
                return ("cmd.exe", $"/c scoop uninstall {Quote(app.Id.Split(':', 2)[^1])}", true);

            case AppSource.Chocolatey:
                return ("cmd.exe", $"/c choco uninstall {Quote(app.Id.Split(':', 2)[^1])} -y", true);
        }

        if (app.MsiProductCode is { } code)
            return ("msiexec.exe", $"/x {code} /qn /norestart", true);

        if (app.QuietUninstallCommand is { Length: > 0 } quiet)
        {
            var (file, arguments) = SplitCommand(quiet);
            return (file, arguments, true);
        }

        if (app.UninstallCommand is { Length: > 0 } command)
        {
            var (file, arguments) = SplitCommand(command);
            return (file, arguments, false);
        }

        return (null, "", false);
    }

    /// <summary>
    /// Splits a registry uninstall string into an executable and its arguments.
    ///
    /// Harder than it looks, and the reason it is its own method with its own tests: the path may
    /// be quoted, may contain spaces without being quoted, and the arguments after it are the
    /// installer author's business. Splitting on the first space would turn
    /// C:\Program Files\Thing\unins.exe into C:\Program with an argument of Files\Thing\unins.exe.
    /// </summary>
    public static (string File, string Arguments) SplitCommand(string command)
    {
        var text = command.Trim();

        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            if (close > 0)
                return (text[1..close], text[(close + 1)..].Trim());
        }

        // Unquoted. The executable ends at ".exe", wherever that falls -- which handles a path
        // with spaces in it, the case a naive split gets wrong.
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
            return (text[..(exe + 4)], text[(exe + 4)..].Trim());

        var space = text.IndexOf(' ');
        return space > 0 ? (text[..space], text[(space + 1)..].Trim()) : (text, "");
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    // ------------------------------------------------------------------ the registry sweep

    /// <summary>
    /// Exports a key to a .reg file, then removes it. Returns null on success, or why not.
    ///
    /// The export is not optional and is not best-effort: a key that could not be backed up is
    /// left alone. This is the registry equivalent of the Recycle Bin, and doing the removal
    /// without it would be doing the irreversible thing at the exact moment the reversible one
    /// turned out to be unavailable.
    /// </summary>
    internal static string? RemoveKey(string path, InstalledApp app)
    {
        string backup;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            backup = Path.Combine(
                BackupDirectory,
                $"{Sanitise(app.Name)}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Sanitise(path)}.reg");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"could not create the backup folder: {e.Message}";
        }

        if (Run("reg.exe", $"export \"{path}\" \"{backup}\" /y") is { } exportFailed)
            return $"could not back the key up, so it was left alone ({exportFailed})";

        if (!File.Exists(backup))
            return "the backup file was not written, so the key was left alone";

        return Run("reg.exe", $"delete \"{path}\" /f");
    }

    /// <summary>Runs a console tool and returns null on success, or a description of the failure.</summary>
    private static string? Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
                return $"{file} would not start";

            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(30_000))
                return $"{file} did not finish";

            return process.ExitCode == 0
                ? null
                : $"{file} exited with {process.ExitCode}{(error.Length > 0 ? ": " + error.Trim() : "")}";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return e.Message;
        }
    }

    public static string Sanitise(string value)
    {
        var text = value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid, '_');

        text = text.Replace('\\', '_').Replace(' ', '_');
        return text.Length > 60 ? text[..60] : text;
    }
}
