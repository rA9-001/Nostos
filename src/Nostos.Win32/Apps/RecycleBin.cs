using System.Runtime.InteropServices;

namespace Nostos.Win32.Apps;

/// <summary>What happened to one thing this program tried to move to the Recycle Bin.</summary>
public sealed record RecycleResult(string Path, bool Recycled, string Message);

/// <summary>
/// Moves files and folders to the Recycle Bin, and never deletes anything.
///
/// This is the piece that lets the uninstaller be thorough without being dangerous. Every other
/// tool of this kind finds leftovers by matching the program's name against folders and registry
/// keys, and then deletes what it found. The matching is guesswork -- a great many games keep
/// their save files in %LOCALAPPDATA%\&lt;GameName&gt;, which is a "leftover" by every rule you
/// could write -- so "thorough" and "deleted my saves" end up being the same code path.
///
/// Recycling instead of deleting makes that guess survivable. The undo is performed by Windows,
/// it is in a place people already know how to look, and it outlives this program entirely.
///
/// <para><b>A failure is never turned into a delete.</b> If the Recycle Bin cannot take
/// something -- a drive with recycling disabled, an item larger than the bin's quota, a path in
/// use -- the item is left exactly where it is and reported. Falling back to a permanent delete
/// at that moment would be doing the dangerous thing precisely when the safe thing was
/// unavailable.</para>
/// </summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHFileOperationW(ref ShFileOpStruct operation);

    private const uint FoDelete = 0x0003;

    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofNoConfirmMkDir = 0x0200;

    /// <summary>
    /// Sends one file or folder to the Recycle Bin.
    ///
    /// Returns rather than throws, because this runs over a list: one folder that is locked by a
    /// running process must not abandon the other eleven, and the reason it failed is something
    /// the reader wants to see next to the row it belongs to.
    /// </summary>
    public static RecycleResult Send(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new RecycleResult(path, false, "no path");

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new RecycleResult(path, false, $"not a usable path: {e.Message}");
        }

        if (!File.Exists(full) && !Directory.Exists(full))
            return new RecycleResult(full, false, "already gone");

        if (IsProtected(full))
            return new RecycleResult(full, false, "refused: this is a Windows directory");

        // The API takes a list of paths terminated by an extra null. A single path still needs
        // the double terminator, and getting that wrong reads past the end of the string.
        var operation = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = full + '\0' + '\0',
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofNoConfirmMkDir,
        };

        int code;
        try
        {
            code = SHFileOperationW(ref operation);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return new RecycleResult(full, false, "the shell would not perform the move");
        }

        if (code != 0)
            return new RecycleResult(full, false, $"the shell refused it (0x{code:x})");

        if (operation.fAnyOperationsAborted != 0)
            return new RecycleResult(full, false, "cancelled");

        // Believe the machine rather than the return code. SHFileOperation can report success
        // for an item it did not move, and a row claiming something is gone when it is still
        // there is the one outcome this whole tab exists to avoid.
        return File.Exists(full) || Directory.Exists(full)
            ? new RecycleResult(full, false, "reported success but it is still there")
            : new RecycleResult(full, true, "moved to the Recycle Bin");
    }

    /// <summary>
    /// Paths this program will not send to the Recycle Bin whatever it was asked.
    ///
    /// A leftover scan is name matching, and name matching can be wrong. It cannot be wrong
    /// about these: a root directory, Windows itself, or the whole of Program Files is never a
    /// leftover, and the cost of being wrong once is the machine.
    /// </summary>
    public static bool IsProtected(string fullPath)
    {
        var path = fullPath.TrimEnd('\\');

        // A drive root: "C:" after trimming.
        if (path.Length <= 3)
            return true;

        // The MSIX package store. Owned by the deployment stack, ACL'd to TrustedInstaller,
        // and the one directory where moving a folder aside corrupts state nothing else can
        // repair. Removing a package is Remove-AppxPackage's job and nobody else's. Found by
        // running the leftover scan over a real machine and reading what it proposed.
        if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(@"\WindowsApps", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Anywhere inside Windows itself, not merely the folder itself.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        if (windows.Length > 0 && path.StartsWith(windows + @"\", StringComparison.OrdinalIgnoreCase))
            return true;

        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ];

        // Equal to one of them is refused; inside one of them is the normal case and is fine.
        return roots.Any(r => r is { Length: > 0 }
                              && string.Equals(path, r.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }
}
