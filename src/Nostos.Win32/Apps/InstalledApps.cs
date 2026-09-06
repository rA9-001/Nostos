using System.Globalization;
using Microsoft.Win32;

namespace Nostos.Win32.Apps;

/// <summary>Where an installed program was found. Determines how it is removed.</summary>
public enum AppSource
{
    /// <summary>HKLM Uninstall, 64-bit view. Installed for everyone.</summary>
    Machine,

    /// <summary>HKLM Uninstall as a 32-bit program sees it. Also installed for everyone.</summary>
    Machine32,

    /// <summary>HKCU Uninstall. Installed for this account only -- Discord, most launchers.</summary>
    User,

    /// <summary>An MSIX package: the Store, and anything else packaged.</summary>
    Store,

    /// <summary>Installed by scoop, into the user's own scoop directory.</summary>
    Scoop,

    /// <summary>Installed by Chocolatey, into ProgramData.</summary>
    Chocolatey,
}

/// <summary>
/// One program on this machine.
/// </summary>
/// <param name="Id">
/// Stable across a refresh and unique across sources, because it is what the window sends back
/// when somebody asks to remove something. Built from the source and the key or package name,
/// never from the display name: two programs are allowed to share a display name, and a
/// removal aimed by name would be aimed at whichever one happened to sort first.
/// </param>
/// <param name="UninstallCommand">The program's own uninstaller. Null for sources that do not use one.</param>
/// <param name="QuietUninstallCommand">
/// The same thing with its own silent switches, when the installer author supplied one. Many
/// did not, which is why removal cannot always be unattended and the window has to say so.
/// </param>
/// <param name="SizeBytes">
/// From the registry's EstimatedSize where present, and 0 where not. Famously approximate and
/// often absent, so it is shown as a hint rather than a fact and never used for arithmetic.
/// </param>
public sealed record InstalledApp(
    string Id,
    string Name,
    AppSource Source,
    string? Publisher = null,
    string? Version = null,
    string? InstallLocation = null,
    string? UninstallCommand = null,
    string? QuietUninstallCommand = null,
    string? IconPath = null,
    long SizeBytes = 0,
    DateOnly? InstalledOn = null)
{
    /// <summary>True when removing this needs the elevated service or an elevated app.</summary>
    public bool IsMachineWide => Source is AppSource.Machine or AppSource.Machine32 or AppSource.Chocolatey;

    /// <summary>True when this can be removed without the user answering an installer's questions.</summary>
    public bool CanRemoveSilently =>
        Source is AppSource.Store or AppSource.Scoop
        || QuietUninstallCommand is { Length: > 0 }
        || MsiProductCode is not null;

    /// <summary>
    /// The MSI product code, when the uninstall command is an MsiExec call.
    ///
    /// Worth pulling out on its own: an MSI uninstall is the one case where a silent removal can
    /// be constructed even though the installer author never supplied a QuietUninstallString.
    /// `MsiExec.exe /I{GUID}` becomes `/X{GUID} /qn /norestart`, which is documented behaviour
    /// of the Windows Installer rather than a guess about somebody's setup.exe.
    /// </summary>
    public string? MsiProductCode => ExtractProductCode(UninstallCommand);

    internal static string? ExtractProductCode(string? command)
    {
        if (command is null || command.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var open = command.IndexOf('{');
        var close = command.IndexOf('}', open + 1);
        if (open < 0 || close < 0)
            return null;

        var code = command[open..(close + 1)];
        return Guid.TryParse(code, out _) ? code : null;
    }
}

/// <summary>
/// Everything installed on this machine, from every source that can be enumerated.
///
/// Windows has no single honest list of this. Settings, Apps shows Store packages and per-user
/// installs; Programs and Features shows machine-wide Win32 and hides Store packages; neither
/// shows what scoop or Chocolatey put there. Answering "what is actually on this PC" means
/// opening three places and still missing things.
///
/// What cannot be enumerated, and is said so on the page rather than quietly omitted: a program
/// that was never installed. A game unzipped into D:\Games has no registry entry, no package
/// and no uninstaller, so there is nothing to find. This lists what registered itself.
/// </summary>
public static class InstalledApps
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string PackageRepository =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Everything, sorted by name, with duplicates across the two HKLM views merged.</summary>
    public static IReadOnlyList<InstalledApp> List()
    {
        var found = new List<InstalledApp>(256);

        found.AddRange(FromUninstallKey(Registry.LocalMachine, UninstallKey, AppSource.Machine));
        found.AddRange(FromUninstallKey(Registry.LocalMachine, UninstallKey32, AppSource.Machine32));
        found.AddRange(FromUninstallKey(Registry.CurrentUser, UninstallKey, AppSource.User));
        found.AddRange(FromPackages());
        found.AddRange(FromScoop());
        found.AddRange(FromChocolatey());

        return Deduplicate(found);
    }

    /// <summary>
    /// Finds one app by the id the window sent back.
    ///
    /// The machine is re-enumerated rather than a cached list being trusted, because this is the
    /// security boundary for removal: the service accepts an id, resolves it here, and refuses
    /// anything that is not currently an installed program. An unprivileged caller cannot use
    /// the removal command to run an arbitrary executable.
    /// </summary>
    public static InstalledApp? Find(string id)
        => List().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Merges the entries that are the same program seen twice.
    ///
    /// The 64-bit and 32-bit HKLM views are separate keys, and a program installed for the
    /// current user as well as machine-wide appears in both. Matched on name and version rather
    /// than on the key, because the key name is exactly what differs. The machine-wide one wins,
    /// because that is the install that has to be removed to actually be rid of it.
    /// </summary>
    private static IReadOnlyList<InstalledApp> Deduplicate(List<InstalledApp> found)
    {
        var byIdentity = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in found.OrderBy(a => a.Source))
        {
            var identity = $"{app.Name}|{app.Version}";
            if (!byIdentity.ContainsKey(identity))
                byIdentity[identity] = app;
        }

        return [.. byIdentity.Values
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Source)];
    }

    // ------------------------------------------------------------------ Win32

    private static IEnumerable<InstalledApp> FromUninstallKey(RegistryKey hive, string path, AppSource source)
    {
        var view = source == AppSource.Machine32 ? RegistryView.Registry32 : RegistryView.Registry64;

        using var root = RegistryKey
            .OpenBaseKey(hive == Registry.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view)
            .OpenSubKey(path);

        if (root is null)
            yield break;

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key is null)
                continue;

            if (Read(key, "DisplayName") is not { Length: > 0 } display)
                continue;

            // Everything below is Windows' own rule for what Programs and Features shows, and it
            // is what turns 295 registry keys on this machine into 77 programs a person would
            // recognise. Without it the list is mostly Visual C++ runtimes and patch records.
            if (Dword(key, "SystemComponent") == 1)
                continue;

            // A patch or update of something else, not a program in its own right.
            if (Read(key, "ParentKeyName") is { Length: > 0 } || Read(key, "ParentDisplayName") is { Length: > 0 })
                continue;

            if (Read(key, "ReleaseType") is { Length: > 0 } release
                && release is "Update" or "Security Update" or "Hotfix" or "ServicePack")
                continue;

            yield return new InstalledApp(
                Id: $"{Prefix(source)}:{name}",
                Name: display.Trim(),
                Source: source,
                Publisher: Read(key, "Publisher"),
                Version: Read(key, "DisplayVersion"),
                InstallLocation: Trim(Read(key, "InstallLocation")),
                UninstallCommand: Read(key, "UninstallString"),
                QuietUninstallCommand: Read(key, "QuietUninstallString"),
                IconPath: IconFrom(Read(key, "DisplayIcon")),
                // EstimatedSize is in kilobytes, and is absent more often than not.
                SizeBytes: (long)(Dword(key, "EstimatedSize") ?? 0) * 1024,
                InstalledOn: ParseInstallDate(Read(key, "InstallDate")));
        }
    }

    private static string Prefix(AppSource source) => source switch
    {
        AppSource.Machine => "machine",
        AppSource.Machine32 => "machine32",
        AppSource.User => "user",
        AppSource.Store => "store",
        AppSource.Scoop => "scoop",
        AppSource.Chocolatey => "choco",
        _ => "other",
    };

    /// <summary>
    /// DisplayIcon is a path, sometimes with a resource index after a comma.
    ///
    /// The comma cannot simply be split on: a path can legitimately contain one, and several
    /// programs on this machine have no index at all. Only a trailing ",<digits>" is an index.
    /// </summary>
    public static string? IconFrom(string? displayIcon)
    {
        if (displayIcon is not { Length: > 0 })
            return null;

        var path = displayIcon.Trim().Trim('"');
        var comma = path.LastIndexOf(',');

        if (comma > 0 && path[(comma + 1)..] is { Length: > 0 } tail
            && (tail[0] == '-' ? tail[1..] : tail).All(char.IsAsciiDigit))
        {
            path = path[..comma];
        }

        return path.Trim().Trim('"') is { Length: > 0 } cleaned ? cleaned : null;
    }

    /// <summary>InstallDate is "yyyyMMdd", when it is there at all and when it is not garbage.</summary>
    public static DateOnly? ParseInstallDate(string? raw)
        => DateOnly.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    // ------------------------------------------------------------------ MSIX

    private static IEnumerable<InstalledApp> FromPackages()
    {
        using var root = Registry.CurrentUser.OpenSubKey(PackageRepository);
        if (root is null)
            yield break;

        foreach (var fullName in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(fullName);
            if (key is null)
                continue;

            var folder = Read(key, "PackageRootFolder");

            // The Windows shell itself: the Start menu, the file picker, the lock screen. These
            // are packages in the same sense the taskbar is a program, and removing one breaks
            // the machine in a way no uninstaller offers to. Not listed at all.
            if (folder is null || folder.Contains(@"\Windows\SystemApps\", StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsFramework(fullName))
                continue;

            yield return new InstalledApp(
                Id: $"store:{fullName}",
                Name: PackageDisplayName(fullName, Read(key, "DisplayName")),
                Source: AppSource.Store,
                Publisher: PublisherFrom(fullName),
                Version: VersionFrom(fullName),
                InstallLocation: folder,
                IconPath: null,
                SizeBytes: 0);
        }
    }

    /// <summary>
    /// A shared runtime rather than something a person installed.
    ///
    /// These are pulled in as dependencies, several apps share one, and removing it breaks
    /// whatever still needs it -- which is exactly the class of thing an uninstaller should not
    /// be inviting somebody to delete from a list of "apps".
    /// </summary>
    public static bool IsFramework(string packageFullName)
    {
        string[] markers =
        [
            ".VCLibs.", ".NET.Native.", "Microsoft.WindowsAppRuntime", "Microsoft.UI.Xaml",
            "Microsoft.Services.Store.Engagement", "Microsoft.Advertising.Xaml",
        ];

        return markers.Any(m => packageFullName.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The name a person would recognise.
    ///
    /// A package's stored DisplayName is often an unresolved resource reference -- literally the
    /// text "ms-resource:AppStoreName" -- because the real string lives in the package's compiled
    /// resource index and is language-dependent. <c>SHLoadIndirectString</c> resolves it, given
    /// the reference rebuilt into the form it expects, which is not the form the registry holds.
    ///
    /// When that fails, the package identity is used instead: "Microsoft.AV1VideoExtension"
    /// becomes "AV1 Video Extension". Not the official name, but a name, and better than showing
    /// somebody a row that reads "ms-resource:AppName".
    /// </summary>
    public static string PackageDisplayName(string packageFullName, string? stored)
    {
        var identity = packageFullName.Split('_')[0];

        if (stored is { Length: > 0 } && !stored.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            && !stored.StartsWith("@{", StringComparison.Ordinal))
        {
            return stored;
        }

        if (stored is { Length: > 0 } && IndirectString.Resolve(packageFullName, identity, stored) is { Length: > 0 } resolved)
            return resolved;

        return Humanise(identity);
    }

    /// <summary>"Microsoft.AV1VideoExtension" -> "AV1 Video Extension".</summary>
    public static string Humanise(string identity)
    {
        var last = identity.Split('.')[^1];
        var text = new System.Text.StringBuilder(last.Length + 8);

        for (var i = 0; i < last.Length; i++)
        {
            // A capital that starts a new word: preceded by a lower-case letter, or the last
            // capital of a run followed by a lower-case one, so "AV1Video" splits once and not
            // three times.
            if (i > 0 && char.IsUpper(last[i])
                && (!char.IsUpper(last[i - 1]) || (i + 1 < last.Length && char.IsLower(last[i + 1]))))
            {
                text.Append(' ');
            }

            text.Append(last[i]);
        }

        return text.ToString();
    }

    internal static string? PublisherFrom(string packageFullName)
    {
        var identity = packageFullName.Split('_')[0];
        var dot = identity.IndexOf('.');
        return dot > 0 ? identity[..dot] : null;
    }

    internal static string? VersionFrom(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out _) ? parts[1] : null;
    }

    // ------------------------------------------------------------------ package managers

    private static IEnumerable<InstalledApp> FromScoop()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps");

        if (!Directory.Exists(root))
            yield break;

        foreach (var directory in SafeDirectories(root))
        {
            var name = Path.GetFileName(directory);

            yield return new InstalledApp(
                Id: $"scoop:{name}",
                Name: name,
                Source: AppSource.Scoop,
                InstallLocation: directory,
                Version: CurrentVersionOf(directory));
        }
    }

    private static IEnumerable<InstalledApp> FromChocolatey()
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "chocolatey", "lib");

        if (!Directory.Exists(root))
            yield break;

        foreach (var directory in SafeDirectories(root))
        {
            var name = Path.GetFileName(directory);

            yield return new InstalledApp(
                Id: $"choco:{name}",
                Name: name,
                Source: AppSource.Chocolatey,
                InstallLocation: directory);
        }
    }

    /// <summary>scoop keeps a "current" junction pointing at the version in use.</summary>
    private static string? CurrentVersionOf(string appDirectory)
    {
        try
        {
            return Directory.EnumerateDirectories(appDirectory)
                .Select(Path.GetFileName)
                .FirstOrDefault(n => n is not null && n != "current");
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Enumeration that survives a directory it cannot read.
    ///
    /// One unreadable folder must not empty the whole list: the tab is the only place somebody
    /// can see what is installed, and a page that shows nothing because of one ACL is worse than
    /// one that shows almost everything.
    /// </summary>
    private static IReadOnlyList<string> SafeDirectories(string root)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(root)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ------------------------------------------------------------------ registry helpers

    private static string? Read(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) as string is { Length: > 0 } text ? text : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static int? Dword(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) as int?;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? Trim(string? path)
        => path is { Length: > 0 } ? path.Trim().TrimEnd('\\') : null;
}
