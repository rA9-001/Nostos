using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace Nostos.Win32.Services;

/// <summary>
/// A permanent CPU priority for an executable, applied by Windows at process creation.
///
/// <see cref="ProcessControl.SetPriority"/> changes a process that is already running, and dies
/// with it. This is the other half: a value under Image File Execution Options that the loader
/// reads every time that image starts, so the game comes up at the priority you chose without
/// anything running to arrange it.
///
/// The key is <c>Image File Execution Options\&lt;image&gt;.exe\PerfOptions\CpuPriorityClass</c>,
/// a DWORD holding one of the <c>PROCESS_PRIORITY_CLASS_*</c> constants -- which are <em>not</em>
/// the same numbers as <see cref="ProcessPriorityClass"/>, and mixing them up is how a machine
/// ends up with a game pinned to Idle. <see cref="ToIfeo"/> is the only place that mapping
/// exists.
///
/// Two things about this key are worth knowing before writing to it. It is machine-wide and
/// matched on the bare file name, so it applies to every <c>cs2.exe</c> on the system, from any
/// folder. And it is the same key whose <c>Debugger</c> value is a well-known malware
/// persistence trick -- nothing here writes, reads or removes <c>Debugger</c>, and
/// <see cref="Clear"/> is careful never to delete a key that holds anything but our own value.
/// </summary>
public static class ImagePriority
{
    private const string IfeoPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    private const string PerfOptions = "PerfOptions";
    private const string ValueName = "CpuPriorityClass";

    /// <summary>
    /// The loader's priority constants, which are its own numbering and not the framework's.
    ///
    /// <c>ProcessPriorityClass.High</c> is 128; <c>PROCESS_PRIORITY_CLASS_HIGH</c> is 3. There is
    /// no Realtime here on purpose: process.game-tuning refuses realtime on a
    /// live process, and a realtime setting that survives reboot and applies before anything can
    /// intervene would be considerably worse.
    /// </summary>
    public static int ToIfeo(ProcessPriorityClass priority) => priority switch
    {
        ProcessPriorityClass.Idle => 1,
        ProcessPriorityClass.Normal => 2,
        ProcessPriorityClass.High => 3,
        ProcessPriorityClass.BelowNormal => 5,
        ProcessPriorityClass.AboveNormal => 6,
        _ => throw new ArgumentOutOfRangeException(
            nameof(priority), priority,
            "Image File Execution Options has no constant for this priority class. Realtime in "
            + "particular is deliberately unreachable here."),
    };

    /// <summary>The reverse, for describing what is already on the machine. Null if unrecognised.</summary>
    public static ProcessPriorityClass? FromIfeo(int value) => value switch
    {
        1 => ProcessPriorityClass.Idle,
        2 => ProcessPriorityClass.Normal,
        3 => ProcessPriorityClass.High,
        5 => ProcessPriorityClass.BelowNormal,
        6 => ProcessPriorityClass.AboveNormal,
        _ => null,
    };

    /// <summary>How a value reads in a status line, falling back to the raw number.</summary>
    public static string Describe(int value)
        => FromIfeo(value)?.ToString() ?? $"unrecognised ({value})";

    /// <summary>
    /// Normalises what a person or a picker supplies into the file name the loader matches on.
    ///
    /// The picker hands over <see cref="Process.ProcessName"/>, which has no extension; somebody
    /// typing it themselves writes "cs2.exe", and somebody who pasted a shortcut target writes
    /// the whole path. The loader matches the bare file name, so all three become the same thing.
    /// </summary>
    public static string NormaliseImageName(string name)
    {
        var trimmed = (name ?? "").Trim().Trim('"');
        if (trimmed.Length == 0)
            throw new ArgumentException("Empty image name.", nameof(name));

        // A path is accepted and reduced to its leaf, because that is all the loader looks at --
        // and silently keeping the folder would write a key that can never match anything.
        var leaf = Path.GetFileName(trimmed);
        if (leaf.Length == 0)
            throw new ArgumentException($"'{name}' names no file.", nameof(name));

        if (!leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            leaf += ".exe";

        // Everything the registry cannot hold in a key name. Checked rather than stripped: a
        // silently altered name would produce a key that looks right and matches nothing.
        if (leaf.Contains('\\') || leaf.Contains('/'))
            throw new ArgumentException($"'{name}' is not a plain file name.", nameof(name));

        return leaf;
    }

    /// <summary>The permanent priority set for one image, or null when it has none.</summary>
    public static int? Read(string imageName)
        => RegistryAccess.ReadDword(Reference(NormaliseImageName(imageName)));

    public static void Set(string imageName, int ifeoValue)
        => RegistryAccess.Write(
            Reference(NormaliseImageName(imageName)), ifeoValue, RegistryValueKind.DWord);

    /// <summary>
    /// Removes the permanent priority for one image, and any empty keys that leaves.
    ///
    /// Deleting the value is the part that matters; the tidying after it is what stops a
    /// reverted machine keeping a shadow of every game it was ever asked about. Both keys are
    /// removed only when they are completely empty, so an image that also has a <c>Debugger</c>
    /// value, a <c>UseLargePages</c>, or anything else set by another tool keeps its key.
    /// </summary>
    public static void Clear(string imageName)
    {
        var image = NormaliseImageName(imageName);
        RegistryAccess.DeleteValue(Reference(image));

        using var baseKey = RegistryAccess.OpenBase("HKLM");
        using var ifeo = baseKey.OpenSubKey(IfeoPath, writable: true);
        if (ifeo is null)
            return;

        if (IsEmpty(ifeo, $@"{image}\{PerfOptions}"))
            ifeo.DeleteSubKey($@"{image}\{PerfOptions}", throwOnMissingSubKey: false);

        if (IsEmpty(ifeo, image))
            ifeo.DeleteSubKey(image, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Every image on this machine with a permanent priority, mapped to its value.
    ///
    /// Includes entries this program did not write. That is the point: the snapshot has to be
    /// the machine as it was, or revert restores a fiction.
    /// </summary>
    public static IReadOnlyDictionary<string, int> All()
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var baseKey = RegistryAccess.OpenBase("HKLM");
        using var ifeo = baseKey.OpenSubKey(IfeoPath, writable: false);
        if (ifeo is null)
            return found;

        foreach (var image in ifeo.GetSubKeyNames())
        {
            try
            {
                using var perf = ifeo.OpenSubKey($@"{image}\{PerfOptions}", writable: false);
                if (perf?.GetValue(ValueName) is int value)
                    found[image] = value;
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
            {
                // IFEO holds a subkey per image name, some written by installers and some by
                // things that would rather not be read. One unreadable entry is not a reason to
                // fail the enumeration -- and an entry we cannot read is one we cannot have set.
            }
        }

        return found;
    }

    private static RegistryValueRef Reference(string image)
        => new("HKLM", $@"{IfeoPath}\{image}\{PerfOptions}", ValueName);

    private static bool IsEmpty(RegistryKey parent, string path)
    {
        using var key = parent.OpenSubKey(path, writable: false);
        return key is not null && key.ValueCount == 0 && key.SubKeyCount == 0;
    }
}
