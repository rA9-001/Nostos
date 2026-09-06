using Microsoft.Win32;

namespace Nostos.Win32.Apps;

public enum LeftoverKind
{
    Folder,
    RegistryKey,
}

/// <summary>
/// One thing an uninstaller left behind.
/// </summary>
/// <param name="Path">The folder, or the registry key in HIVE\subkey form.</param>
/// <param name="HoldsUserData">
/// True when the folder looks like it holds something the person made rather than something the
/// installer wrote: saves, screenshots, configuration they edited. Everything here goes to the
/// Recycle Bin either way, so this is not a safety mechanism -- it is the row that deserves to
/// be read before it scrolls past.
/// </param>
public sealed record Leftover(
    LeftoverKind Kind,
    string Path,
    long SizeBytes = 0,
    bool HoldsUserData = false);

/// <summary>
/// Finds what an uninstaller left behind.
///
/// This is name matching, and it is worth being honest that name matching is guesswork. The only
/// way to know for certain what an installer created is to have watched it install, which needs
/// a filesystem filter driver -- a kernel driver, which this program will not ship.
///
/// So the guessing is made as narrow as it can be, and everything it finds goes to the Recycle
/// Bin rather than being deleted. Two rules do most of the work:
///
/// <list type="bullet">
/// <item>A candidate must match the program's name <b>exactly</b>, never as a substring.
/// Substring matching is how uninstalling "Microsoft Teams" proposes deleting
/// %LOCALAPPDATA%\Microsoft, taking Edge, OneDrive and the Windows shell's own state with
/// it.</item>
/// <item>Only a fixed set of parent directories is looked in. The scan never walks the disk, so
/// it cannot wander into a folder that merely happens to share a word with the program.</item>
/// </list>
/// </summary>
public static class Leftovers
{
    /// <summary>Everything that still mentions this program, after its uninstaller has run.</summary>
    public static IReadOnlyList<Leftover> Scan(InstalledApp app)
    {
        if (!IsWorthScanning(app.Source))
            return [];

        var found = new List<Leftover>();

        foreach (var folder in FolderCandidates(app))
        {
            if (Directory.Exists(folder) && !RecycleBin.IsProtected(folder))
                found.Add(new Leftover(LeftoverKind.Folder, folder, SizeOf(folder), LooksLikeUserData(folder)));
        }

        foreach (var key in RegistryCandidates(app))
        {
            if (KeyExists(key))
                found.Add(new Leftover(LeftoverKind.RegistryKey, key));
        }

        return [.. found
            .DistinctBy(l => l.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l.Kind)
            .ThenBy(l => l.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Only Win32 installers leave leftovers worth hunting for.
    ///
    /// Found by running the scan over everything on a real machine and reading what it proposed.
    /// MSIX packages are self-contained by design -- the format exists so that removal is clean
    /// -- so a scan over one finds no true leftovers and two kinds of false one: the package's
    /// own directory under Program Files\WindowsApps, which belongs to the deployment stack and
    /// must never be touched by anything else, and system keys that merely share the package's
    /// name. The Store package called "DirectX" proposed HKLM\SOFTWARE\Microsoft\DirectX,
    /// which is Windows' own.
    ///
    /// scoop and Chocolatey own their directories in the same way and clean up after themselves.
    /// So the scan runs only where there is something to find.
    /// </summary>
    public static bool IsWorthScanning(AppSource source)
        => source is AppSource.Machine or AppSource.Machine32 or AppSource.User;

    /// <summary>
    /// The folders worth looking at. A fixed list, never a search.
    ///
    /// InstallLocation is included because a surprising number of uninstallers leave their own
    /// directory standing with a log file or an empty tree in it.
    /// </summary>
    public static IEnumerable<string> FolderCandidates(InstalledApp app)
    {
        if (app.InstallLocation is { Length: > 0 } installed)
            yield return installed;

        string[] parents =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"),
        ];

        foreach (var parent in parents.Where(p => p is { Length: > 0 }))
        {
            foreach (var name in NameVariants(app.Name))
                yield return Path.Combine(parent, name);

            // Publisher\Program, which is the other common shape: Valve\Steam, NVIDIA\GeForce.
            if (app.Publisher is { Length: > 0 } publisher)
            {
                foreach (var name in NameVariants(app.Name))
                    yield return Path.Combine(parent, Clean(publisher), name);
            }
        }
    }

    /// <summary>
    /// Registry keys worth looking at. Also a fixed list.
    ///
    /// Deliberately does not include a search of HKCU\Software for anything containing the name.
    /// That search is what other tools do, it is where their worst outcomes come from, and the
    /// keys it finds beyond this list are almost always file-association and MRU entries that
    /// Windows tidies up on its own.
    /// </summary>
    public static IEnumerable<string> RegistryCandidates(InstalledApp app)
    {
        string[] roots = [@"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node"];

        foreach (var root in roots)
        {
            foreach (var name in NameVariants(app.Name))
                yield return $@"{root}\{name}";

            if (app.Publisher is { Length: > 0 } publisher)
            {
                foreach (var name in NameVariants(app.Name))
                    yield return $@"{root}\{Clean(publisher)}\{name}";
            }
        }

        // The uninstall entry itself, when the uninstaller failed to remove its own record. This
        // is the one that leaves a ghost in Programs and Features that cannot be uninstalled
        // because the uninstaller it points at is already gone.
        var uninstall = app.Source switch
        {
            AppSource.Machine => @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            AppSource.Machine32 => @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            AppSource.User => @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            _ => null,
        };

        if (uninstall is not null && app.Id.Split(':', 2) is [_, { Length: > 0 } keyName])
            yield return $@"{uninstall}\{keyName}";
    }

    /// <summary>
    /// The forms of a program's name that a folder might actually be called.
    ///
    /// "Battle.net" is a directory called "Battle.net". "EA Desktop" is sometimes "EADesktop".
    /// A trailing version, and the (R) and (TM) marks installers put in DisplayName, are never
    /// part of the directory name.
    ///
    /// Every variant still has to match a directory name in full. Shortening the name is about
    /// finding the right folder, not about widening the net.
    /// </summary>
    public static IEnumerable<string> NameVariants(string name)
    {
        var cleaned = Clean(name);
        if (cleaned.Length < 3)
            yield break;

        yield return cleaned;

        var tight = cleaned.Replace(" ", "");
        if (tight.Length >= 3 && tight != cleaned)
            yield return tight;
    }

    /// <summary>Strips the marks and the trailing version installers put in a display name.</summary>
    public static string Clean(string name)
    {
        var text = name
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("®", "", StringComparison.Ordinal)
            .Replace("™", "", StringComparison.Ordinal)
            .Trim();

        // A trailing "1.2.3" or "v1.2": part of the display name, never part of the folder.
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && Version.TryParse(words[^1].TrimStart('v', 'V'), out _))
            text = string.Join(' ', words[..^1]);

        foreach (var invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid.ToString(), "", StringComparison.Ordinal);

        return text.Trim();
    }

    /// <summary>
    /// Does this folder look like it holds something the person made?
    ///
    /// Only used to mark the row so it gets read rather than scrolled past. It is not a safety
    /// mechanism and must not be treated as one -- these folders go to the Recycle Bin like
    /// everything else, and the Recycle Bin is the safety mechanism.
    /// </summary>
    public static bool LooksLikeUserData(string folder)
    {
        string[] markers = ["save", "saves", "savegame", "savegames", "screenshots", "profiles", "config", "replays"];

        try
        {
            if (markers.Any(m => Directory.Exists(Path.Combine(folder, m))))
                return true;

            return Directory.EnumerateFiles(folder, "*.sav", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// How big a folder is, for the row.
    ///
    /// Walked rather than guessed, and capped: a leftover scan should not spend thirty seconds
    /// measuring a 200,000-file directory somebody is about to recycle anyway.
    /// </summary>
    internal static long SizeOf(string folder, int fileLimit = 20_000)
    {
        long total = 0;
        var counted = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (++counted > fileLimit)
                    break;

                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished between the enumeration and the measurement.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }

    internal static bool KeyExists(string path)
    {
        var (hive, subKey) = Split(path);
        if (hive is null)
            return false;

        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static (RegistryKey? Hive, string SubKey) Split(string path)
    {
        var slash = path.IndexOf('\\');
        if (slash < 0)
            return (null, "");

        var hive = path[..slash] switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => null,
        };

        return (hive, path[(slash + 1)..]);
    }
}
