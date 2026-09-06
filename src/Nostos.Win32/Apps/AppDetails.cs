using Nostos.Win32.Services;

namespace Nostos.Win32.Apps;

/// <summary>One thing a program's registration claims about itself.</summary>
public enum EvidenceItem
{
    /// <summary>The entry that puts it in the list at all.</summary>
    Registration,

    /// <summary>Where it says it was installed.</summary>
    InstallFolder,

    /// <summary>The uninstaller it nominated.</summary>
    Uninstaller,

    /// <summary>The icon it nominated.</summary>
    Icon,
}

/// <summary>What a removable thing is, which decides how it is taken away.</summary>
public enum RemovableKind
{
    /// <summary>A folder. Goes to the Recycle Bin.</summary>
    Folder,

    /// <summary>A registry key. Exported to a .reg file, then removed.</summary>
    RegistryKey,

    /// <summary>A single file. Goes to the Recycle Bin.</summary>
    File,
}

/// <summary>
/// One thing belonging to this program that could be taken away on its own.
/// </summary>
/// <param name="IsRegistration">
/// True for the entry that puts the row in the list. Worth marking rather than inferring,
/// because it is the only item whose removal makes the row itself disappear.
/// </param>
/// <param name="NeedsAdmin">
/// True when this one is outside the reach of the process that read it: an HKLM key, or a folder
/// under Program Files, ProgramData or Windows, while not elevated. Decided at read time, in the
/// process that would do the removing, so the panel can say so before the button is pressed
/// rather than after reg.exe has returned access denied.
/// </param>
public sealed record RemovableItem(
    RemovableKind Kind,
    string Path,
    long SizeBytes = 0,
    bool HoldsUserData = false,
    bool IsRegistration = false,
    bool NeedsAdmin = false);

/// <summary>
/// A claim the registration makes, and whether it is still true.
/// </summary>
/// <param name="Present">
/// Whether the thing at <paramref name="Path"/> is actually there. This is the whole point: an
/// entry whose every path is missing is a leftover registration rather than a program.
/// </param>
public sealed record AppEvidence(EvidenceItem Item, string Path, bool Present);

/// <summary>
/// Everything known about why one row is in the list, and what of it is still on the machine.
/// </summary>
/// <param name="Registration">The registry key or package that causes the row to exist.</param>
/// <param name="CanForget">
/// Whether the registration is something this program could remove on its own. True for the
/// three Uninstall keys and false for everything else: a Store package, a scoop directory or a
/// Chocolatey directory is not a stale note about a program, it is the program.
/// </param>
public sealed record AppDetail(
    string Id,
    string Name,
    string Registration,
    IReadOnlyList<AppEvidence> Evidence,
    IReadOnlyList<Leftover> Leftovers,
    IReadOnlyList<RemovableItem> Removable,
    bool CanForget)
{
    /// <summary>Recorded paths that are no longer there.</summary>
    public int MissingCount => Evidence.Count(e => e.Item != EvidenceItem.Registration && !e.Present);

    /// <summary>Recorded paths, not counting the registration itself.</summary>
    public int CheckedCount => Evidence.Count(e => e.Item != EvidenceItem.Registration);

    /// <summary>
    /// Nothing the entry points at still exists.
    ///
    /// The strong case: an uninstaller removed the program's files and its own registration
    /// outlived them, so the row describes something that is not there. It is stated as an
    /// observation rather than acted on -- what to do about it is the reader's decision, and
    /// they know things about their machine that this does not.
    /// </summary>
    public bool NothingLeft => CheckedCount > 0 && MissingCount == CheckedCount;

    public long LeftoverBytes => Leftovers.Sum(l => l.SizeBytes);

    /// <summary>
    /// True when removing the registration itself is out of reach without administrator rights.
    ///
    /// A machine-wide program registers under HKLM, and the entry is what Forget removes, so
    /// this is the one fact that button needs about itself.
    /// </summary>
    public bool ForgetNeedsAdmin => Removable.Any(i => i.IsRegistration && i.NeedsAdmin);
}

/// <summary>
/// Answers "why is this in the list, and what of it is still here?"
///
/// The question is worth answering directly because the list is only as trustworthy as its
/// weakest row, and its weakest rows are the ones nobody can explain. A program uninstalled
/// months ago can still appear, because what puts it in the list is a registry key rather than
/// the program, and uninstallers do sometimes leave theirs behind. Without this the only thing
/// the window can say is that Windows thinks it is installed -- which, when the reader knows it
/// is not, reads as the list being wrong rather than the registry being stale.
///
/// <para>Every check here is a read. Nothing is opened, started or changed.</para>
/// </summary>
public static class AppDetails
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string PackageRepository =
        @"HKEY_CURRENT_USER\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Reads the machine for one program. Safe to call on any thread.</summary>
    /// <param name="elevated">
    /// Whether the caller could write machine-wide state. Defaults to asking, and is a parameter
    /// only so a test can pose both answers -- the same shape as the cleanup scan, and for the
    /// same reason: what this program is allowed to do is not something a test can arrange.
    /// </param>
    public static AppDetail For(InstalledApp app, bool? elevated = null)
    {
        var privileged = elevated ?? WindowsPrivilegeCheck.Instance.IsElevated;

        var evidence = new List<AppEvidence> { new(EvidenceItem.Registration, Registration(app), true) };

        if (app.InstallLocation is { Length: > 0 } folder)
            evidence.Add(new AppEvidence(EvidenceItem.InstallFolder, folder, DirectoryExists(folder)));

        if (AppRemoval.CommandFor(app) is { File: { Length: > 0 } uninstaller })
        {
            var resolved = Resolve(uninstaller);
            evidence.Add(new AppEvidence(EvidenceItem.Uninstaller, resolved, FileExists(resolved)));
        }

        if (app.IconPath is { Length: > 0 } icon)
            evidence.Add(new AppEvidence(EvidenceItem.Icon, icon, FileExists(icon)));

        // The registration is filtered out of the leftovers because the scan finds it too: it is
        // a registry key under the program's name, which is exactly what that scan looks for.
        // Shown in both places it would read as two separate things to deal with, when it is one
        // -- and the panel's own answer to "why is this listed" is already that key.
        var registration = evidence[0].Path;
        var leftovers = Leftovers.Scan(app)
            .Where(l => !AppRemoval.IsSameKey(l.Path, registration))
            .ToList();

        return new AppDetail(
            app.Id, app.Name, registration, evidence, leftovers,
            Removables(evidence, leftovers, registration, CanForget(app), privileged),
            CanForget(app));
    }

    /// <summary>
    /// Everything belonging to this program that could be taken away, one item at a time.
    ///
    /// Three sources, and the order is the order somebody would want to work through them:
    /// what was found on disk first, then a recorded file that the scan could not have found,
    /// then the registration -- last, because removing it takes the row out of the list and with
    /// it the panel listing everything else.
    ///
    /// <para>An entry's icon earns its place here. A stale entry often points at a file under
    /// the publisher's folder rather than the program's -- Riot keeps a game's icon in
    /// <c>ProgramData\Riot Games\Metadata\...</c> -- and the leftover scan will not propose that,
    /// because it matches on the program's name and refuses substrings. It is a real leftover
    /// that nothing else here would offer to clear up.</para>
    ///
    /// <para>The uninstaller is deliberately <b>not</b> offered. Deleting it strands a program
    /// that is still installed with no supported way to remove it, which is a worse position
    /// than the one somebody opened this panel to get out of.</para>
    /// </summary>
    private static IReadOnlyList<RemovableItem> Removables(
        IReadOnlyList<AppEvidence> evidence,
        IReadOnlyList<Leftover> leftovers,
        string registration,
        bool canForget,
        bool elevated)
    {
        var items = new List<RemovableItem>();

        foreach (var leftover in leftovers)
        {
            items.Add(new RemovableItem(
                leftover.Kind == LeftoverKind.Folder ? RemovableKind.Folder : RemovableKind.RegistryKey,
                leftover.Path,
                leftover.SizeBytes,
                leftover.HoldsUserData,
                NeedsAdmin: !elevated && IsMachineWide(leftover.Path)));
        }

        if (evidence.FirstOrDefault(e => e.Item == EvidenceItem.Icon) is { Present: true } icon
            && !Inside(icon.Path, items))
        {
            items.Add(new RemovableItem(
                RemovableKind.File, icon.Path, SizeOf(icon.Path),
                NeedsAdmin: !elevated && IsMachineWide(icon.Path)));
        }

        if (canForget)
        {
            items.Add(new RemovableItem(
                RemovableKind.RegistryKey, registration, IsRegistration: true,
                NeedsAdmin: !elevated && IsMachineWide(registration)));
        }

        return items;
    }

    /// <summary>
    /// Whether removing this needs rights an ordinary account does not have.
    ///
    /// Decided from the path, which is the only thing available before the attempt and is
    /// reliable for the cases that matter: HKLM, and the machine-wide directories. It errs
    /// towards saying nothing. A user-owned path that happens to be protected some other way
    /// still fails with whatever the API says, which is what happened before this existed;
    /// claiming administrator rights are needed when they are not would be the worse error,
    /// because it teaches somebody to restart the program elevated out of habit.
    /// </summary>
    public static bool IsMachineWide(string path)
    {
        if (path.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("HKCR", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Separators are normalised because the registry is written by hand as often as by a
        // tool: a leftover recorded as C:/ProgramData/... is a real thing this has seen.
        var normalised = path.Replace('/', '\\');

        return MachineFolders().Any(folder =>
            folder.Length > 0
            && normalised.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The directories an ordinary account cannot write to.</summary>
    private static IEnumerable<string> MachineFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }

    /// <summary>
    /// Whether a file is already covered by a folder in the list.
    ///
    /// Offering a file and the folder containing it as two separate things to remove is offering
    /// the same removal twice, and the second one fails confusingly once the first has run.
    /// </summary>
    private static bool Inside(string path, IEnumerable<RemovableItem> items)
    {
        // Separators are normalised because the registry is written by hand as often as by a
        // tool: Riot records its icon as C:/ProgramData/... with forward slashes, which Windows
        // accepts and a string comparison does not.
        var file = Normalise(path);

        return items.Any(i => i.Kind == RemovableKind.Folder
                              && file.StartsWith(Normalise(i.Path).TrimEnd('\\') + "\\",
                                  StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The exact thing that puts this row in the list, written out in full.
    ///
    /// In full because it is the answer to the question. "It came from the registry" explains
    /// nothing; a key path can be pasted into regedit and looked at, which is what somebody who
    /// does not believe the row is going to want to do.
    /// </summary>
    public static string Registration(InstalledApp app)
    {
        var key = app.Id.Split(':', 2) is [_, var name] ? name : app.Id;

        return app.Source switch
        {
            AppSource.Machine => $@"HKEY_LOCAL_MACHINE\{UninstallKey}\{key}",
            AppSource.Machine32 => $@"HKEY_LOCAL_MACHINE\{UninstallKey32}\{key}",
            AppSource.User => $@"HKEY_CURRENT_USER\{UninstallKey}\{key}",
            AppSource.Store => $@"{PackageRepository}\{key}",
            _ => app.InstallLocation ?? key,
        };
    }

    /// <summary>
    /// Whether the registration is a note about a program rather than the program itself.
    ///
    /// Only the three Uninstall keys qualify. A Store package, a scoop app directory or a
    /// Chocolatey one is the installed thing, so "remove the registration and leave the rest"
    /// is not a coherent action there -- it would hide something that is still installed, which
    /// is the opposite of what this window is for.
    /// </summary>
    public static bool CanForget(InstalledApp app)
        => app.Source is AppSource.Machine or AppSource.Machine32 or AppSource.User;

    /// <summary>
    /// An uninstall command's executable as a full path.
    ///
    /// Necessary because a great many entries name theirs without one -- "MsiExec.exe /X{...}"
    /// is the commonest uninstall string on any machine -- and Windows resolves those against
    /// the search path when it starts them. Checking the bare name for existence would resolve
    /// it against the working directory instead, so every MSI product on the machine would be
    /// reported as having a missing uninstaller: fifteen rows on this one, each of them wrong,
    /// in the exact place somebody has come to find out whether a row can be trusted.
    /// </summary>
    public static string Resolve(string file)
    {
        if (Path.IsPathRooted(file))
            return file;

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidates = new List<string>();

        if (system is { Length: > 0 })
            candidates.Add(Path.Combine(system, file));

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (directory is { Length: > 0 })
                candidates.Add(Path.Combine(directory, file));
        }

        foreach (var candidate in candidates)
        {
            if (FileExists(candidate))
                return candidate;
        }

        return file;
    }

    private static bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
