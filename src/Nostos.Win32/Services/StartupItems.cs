using Microsoft.Win32;

namespace Nostos.Win32.Services;

/// <summary>Where Windows found something to launch at sign-in.</summary>
public enum StartupSource
{
    /// <summary>HKLM Run, 64-bit view. Applies to every account on the machine.</summary>
    MachineRun,

    /// <summary>HKLM Run as a 32-bit program sees it (Wow6432Node). A separate list, not a view of the first.</summary>
    MachineRun32,

    /// <summary>HKCU Run. This account only.</summary>
    UserRun,

    /// <summary>A shortcut in the all-users Startup folder.</summary>
    MachineStartupFolder,

    /// <summary>A shortcut in this account's Startup folder.</summary>
    UserStartupFolder,
}

/// <summary>One thing that runs when you sign in.</summary>
/// <param name="Id">Stable identifier, e.g. <c>user-run:Steam</c>. What the UI and the pipe pass around.</param>
/// <param name="Name">The Run value name, or the shortcut's file name.</param>
/// <param name="Command">The command line exactly as Windows stored it.</param>
/// <param name="ExecutablePath">The file the icon comes from, or null when the command cannot be resolved.</param>
/// <param name="IsEnabled">False when Windows has been told to skip it.</param>
/// <param name="Location">Where it lives, written the way a person would look for it.</param>
public sealed record StartupItem(
    string Id,
    string Name,
    StartupSource Source,
    string Command,
    string? ExecutablePath,
    bool IsEnabled,
    string Location)
{
    /// <summary>True when turning this off affects every account, and so needs the service.</summary>
    public bool IsMachineWide =>
        Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.MachineStartupFolder;
}

/// <summary>
/// The list of things that start with Windows, and the switch that turns one off.
///
/// Nothing here deletes anything. Windows keeps its own record of which startup entries the user
/// has switched off -- under <c>Explorer\StartupApproved</c> -- and that is what Task Manager's
/// Startup tab writes. Using the same mechanism means three things worth having: the entry
/// itself is never destroyed, so turning it back on restores exactly what was there; Task
/// Manager and this program agree about the state, instead of each showing half the truth; and
/// an uninstall of Nostos leaves nothing behind that Windows cannot manage on its own.
///
/// The alternative -- deleting the Run value and remembering it in our own journal -- is what
/// most debloaters do, and it is why uninstalling one of them leaves a machine that has quietly
/// forgotten how to start its own audio driver.
/// </summary>
public static class StartupItems
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private const string ApprovedKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    // ---------------------------------------------------------------- reading

    public static IReadOnlyList<StartupItem> List()
    {
        var found = new List<StartupItem>();

        found.AddRange(FromRunKey(StartupSource.MachineRun));
        found.AddRange(FromRunKey(StartupSource.MachineRun32));
        found.AddRange(FromRunKey(StartupSource.UserRun));
        found.AddRange(FromFolder(StartupSource.MachineStartupFolder));
        found.AddRange(FromFolder(StartupSource.UserStartupFolder));

        // By name, because that is what the reader is scanning for. Grouping by source first was
        // tried and read worse: somebody looking for Steam does not know or care which of five
        // places Windows keeps it in, and the location is on the row anyway.
        return [.. found.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<StartupItem> FromRunKey(StartupSource source)
    {
        var items = new List<StartupItem>();

        using var baseKey = RegistryKey.OpenBaseKey(HiveOf(source), ViewOf(source));
        using var key = baseKey.OpenSubKey(RunKey, writable: false);
        if (key is null)
            return items;

        var approvals = Approvals(source);

        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0)
                continue;

            var command = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string ?? "";

            items.Add(new StartupItem(
                Id: $"{Prefix(source)}:{name}",
                Name: name,
                Source: source,
                Command: command,
                ExecutablePath: ResolveExecutable(command),
                IsEnabled: IsEnabled(approvals, name),
                Location: LocationOf(source)));
        }

        return items;
    }

    private static IEnumerable<StartupItem> FromFolder(StartupSource source)
    {
        var items = new List<StartupItem>();
        var folder = FolderOf(source);

        if (folder.Length == 0 || !Directory.Exists(folder))
            return items;

        var approvals = Approvals(source);

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);

            // Explorer's own folder metadata, not a startup item.
            if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                continue;

            items.Add(new StartupItem(
                Id: $"{Prefix(source)}:{name}",
                Name: Path.GetFileNameWithoutExtension(file),
                Source: source,
                Command: file,
                // The shortcut itself, not its target. Resolving a .lnk needs COM, and the icon
                // Windows draws for the shortcut is the target's icon anyway.
                ExecutablePath: file,
                IsEnabled: IsEnabled(approvals, name),
                Location: LocationOf(source)));
        }

        return items;
    }

    // ---------------------------------------------------------------- writing

    /// <summary>
    /// Turns one entry on or off, the way Task Manager does.
    ///
    /// Takes the item rather than an id so the caller has to have read it first: on the service
    /// side of the pipe that is the difference between "switch off a startup entry that exists"
    /// and "write these bytes into HKLM", and only one of those is a thing a privileged process
    /// should accept from an unprivileged one.
    /// </summary>
    public static void SetEnabled(StartupItem item, bool enabled)
    {
        var approvalName = ApprovalNameOf(item);

        using var baseKey = RegistryKey.OpenBaseKey(HiveOf(item.Source), ViewOf(item.Source));
        using var key = baseKey.CreateSubKey($@"{ApprovedKey}\{LeafOf(item.Source)}", writable: true)
            ?? throw new InvalidOperationException(
                $"Could not open the StartupApproved key for {item.Source}.");

        // Start from whatever is already there rather than from a constant. The first byte is a
        // bitmask, not a flag -- this machine has an entry sitting at 0x07 -- and the bits other
        // than the low one are undocumented. Preserving them is free; guessing at them is not.
        var existing = key.GetValue(approvalName) as byte[];
        var value = existing is { Length: 12 } ? (byte[])existing.Clone() : new byte[12];

        if (enabled)
        {
            value[0] &= 0xFE;

            // Task Manager zeroes the timestamp when re-enabling: the field records when the
            // entry was switched off, and it was not.
            Array.Clear(value, 1, 11);
        }
        else
        {
            value[0] |= 0x01;

            if (value[0] == 0x01)
                value[0] = 0x03;   // Nothing was there before; 0x03 is what Windows writes.

            BitConverter.TryWriteBytes(value.AsSpan(4), DateTime.UtcNow.ToFileTimeUtc());
        }

        key.SetValue(approvalName, value, RegistryValueKind.Binary);
    }

    // ---------------------------------------------------------------- approval state

    /// <summary>
    /// Whether Windows will actually run an entry, given its approval record.
    ///
    /// **Bit 0 of the first byte is the disabled flag**, and the rest of the byte is not an enum:
    /// 0x02 and 0x06 both mean enabled, 0x03 and 0x07 both mean disabled. Reading the byte as a
    /// value rather than a mask is the bug this comment exists to prevent -- it works on every
    /// machine where the byte happens to be 0x02 or 0x03, which is most of them, and then
    /// silently reports one entry backwards on a machine like the one this was written on.
    ///
    /// No record at all means enabled: the approval key only gains an entry once something has
    /// switched it off.
    /// </summary>
    public static bool IsApprovedEnabled(byte[]? approval)
        => approval is not { Length: > 0 } || (approval[0] & 0x01) == 0;

    private static bool IsEnabled(IReadOnlyDictionary<string, byte[]> approvals, string name)
        => IsApprovedEnabled(approvals.GetValueOrDefault(name));

    private static IReadOnlyDictionary<string, byte[]> Approvals(StartupSource source)
    {
        var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(HiveOf(source), ViewOf(source));
            using var key = baseKey.OpenSubKey($@"{ApprovedKey}\{LeafOf(source)}", writable: false);
            if (key is null)
                return found;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] value)
                    found[name] = value;
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Unreadable approvals mean every entry reads as enabled, which is the same thing the
            // user would see in Task Manager if it could not read them either.
        }

        return found;
    }

    /// <summary>
    /// The name an entry is filed under in the approval key: the Run value name, or the
    /// shortcut's file name including its extension.
    /// </summary>
    private static string ApprovalNameOf(StartupItem item)
        => item.Source is StartupSource.MachineStartupFolder or StartupSource.UserStartupFolder
            ? Path.GetFileName(item.Command)
            : item.Name;

    // ---------------------------------------------------------------- command lines

    /// <summary>
    /// The executable a Run command line points at, for the icon and for the path shown on the row.
    ///
    /// Deliberately conservative: a Run command can be a quoted path with arguments, a bare path
    /// with spaces and no quotes, a rundll32 invocation, or something with environment variables
    /// in it. Anything this cannot resolve to a file that exists returns null and the row simply
    /// shows no icon, which is a better outcome than an icon taken from the wrong file.
    /// </summary>
    public static string? ResolveExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var text = Environment.ExpandEnvironmentVariables(command.Trim());

        // Quoted: the quotes say exactly where the path ends, so there is nothing to guess and
        // no existence check to make. Checking anyway was wrong -- a Store app's launcher under
        // WindowsApps is a zero-length reparse point that File.Exists reports as absent, which
        // silently dropped the path and the icon for every Store-installed startup entry.
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text[1..];
        }

        // Unquoted with no spaces is equally unambiguous, whether or not the file can be
        // stat-ed from here.
        if (!text.Contains(' '))
            return text;

        // Unquoted and possibly containing spaces, which is ambiguous by construction:
        // `C:\Program Files\A B\c.exe /x` and `C:\Program.exe Files\A B\c.exe` are the same
        // string. Try the longest prefix first, so a real path with spaces wins over a shorter
        // one that happens to exist.
        for (var i = text.Length; i > 0; i--)
        {
            if (i != text.Length && text[i] != ' ')
                continue;

            var candidate = text[..i].TrimEnd();
            if (candidate.Length > 0 && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    // ---------------------------------------------------------------- source plumbing

    private static RegistryHive HiveOf(StartupSource source) => source switch
    {
        StartupSource.UserRun or StartupSource.UserStartupFolder => RegistryHive.CurrentUser,
        _ => RegistryHive.LocalMachine,
    };

    private static RegistryView ViewOf(StartupSource source)
        => source == StartupSource.MachineRun32 ? RegistryView.Registry32 : RegistryView.Registry64;

    /// <summary>Which subkey of StartupApproved holds this source's records.</summary>
    private static string LeafOf(StartupSource source) => source switch
    {
        StartupSource.MachineRun32 => "Run32",
        StartupSource.MachineStartupFolder or StartupSource.UserStartupFolder => "StartupFolder",
        _ => "Run",
    };

    private static string Prefix(StartupSource source) => source switch
    {
        StartupSource.MachineRun => "machine-run",
        StartupSource.MachineRun32 => "machine-run32",
        StartupSource.UserRun => "user-run",
        StartupSource.MachineStartupFolder => "machine-folder",
        StartupSource.UserStartupFolder => "user-folder",
        _ => "unknown",
    };

    private static string FolderOf(StartupSource source) => source switch
    {
        StartupSource.UserStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        StartupSource.MachineStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        _ => "",
    };

    /// <summary>Where the entry lives, written the way somebody would go and look for it.</summary>
    public static string LocationOf(StartupSource source) => source switch
    {
        StartupSource.MachineRun => @"HKLM\...\CurrentVersion\Run",
        StartupSource.MachineRun32 => @"HKLM\...\CurrentVersion\Run (32-bit)",
        StartupSource.UserRun => @"HKCU\...\CurrentVersion\Run",
        StartupSource.MachineStartupFolder => "Startup folder (all users)",
        StartupSource.UserStartupFolder => "Startup folder",
        _ => "",
    };
}
