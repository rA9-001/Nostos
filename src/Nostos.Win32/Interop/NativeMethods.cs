using System.Runtime.InteropServices;

namespace Nostos.Win32.Interop;

/// <summary>
/// Hand-written P/Invoke surface.
///
/// Kept small and explicit rather than pulled in via a generator: every signature here is a
/// documented, supported Win32 API that operates on the OS from the outside. Nothing in this
/// file injects code, hooks, or writes to another process's memory — that boundary is what
/// keeps the program compatible with kernel anti-cheat.
/// </summary>
internal static partial class NativeMethods
{
    // ------------------------------------------------------------ power schemes

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerDuplicateScheme(
        IntPtr rootPowerKey, in Guid sourceSchemeGuid, ref IntPtr destinationSchemeGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerDeleteScheme(IntPtr rootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName")]
    internal static partial uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        in Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr hMem);

    // ---------------------------------------------------------------- processes

    [Flags]
    internal enum ProcessAccess : uint
    {
        QueryLimitedInformation = 0x1000,
        SetInformation = 0x0200,
        SetQuota = 0x0100,
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(
        ProcessAccess desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    /// <summary>PROCESS_INFORMATION_CLASS. Only the member we use is declared.</summary>
    internal enum ProcessInformationClass
    {
        ProcessPowerThrottling = 4,
    }

    internal const uint ProcessPowerThrottlingCurrentVersion = 1;

    /// <summary>Opt the process in or out of EcoQoS (the scheduler's efficiency mode).</summary>
    internal const uint ProcessPowerThrottlingExecutionSpeed = 0x1;

    /// <summary>Opt the process out of timer-resolution requests being ignored.</summary>
    internal const uint ProcessPowerThrottlingIgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessPowerThrottlingState
    {
        public uint Version;

        /// <summary>Which policies this call is expressing an opinion about.</summary>
        public uint ControlMask;

        /// <summary>For each controlled policy: 1 = throttle, 0 = do not throttle.</summary>
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessInformation(
        IntPtr process,
        ProcessInformationClass informationClass,
        ref ProcessPowerThrottlingState information,
        uint informationSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessInformation(
        IntPtr process,
        ProcessInformationClass informationClass,
        ref ProcessPowerThrottlingState information,
        uint informationSize);

    // ------------------------------------------------------------------ battery

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;

        /// <summary>128 means "no system battery", which is how we tell a desktop from a laptop.</summary>
        public byte BatteryFlag;

        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    // ---------------------------------------------------------- timer resolution

    /// <summary>
    /// Undocumented but stable since NT 3.1 and the only way to read the current resolution.
    /// Values are in 100ns units.
    /// </summary>
    [LibraryImport("ntdll.dll")]
    internal static partial int NtQueryTimerResolution(
        out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtSetTimerResolution(
        uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint currentResolution);

    // ------------------------------------------------------------------ icons

    // Reading the icon out of an executable, for the startup list. All of it operates on a file
    // on disk, not on a running process: nothing here opens, reads or writes another process.

    internal const uint ShgfiIcon = 0x000000100;
    internal const uint ShgfiLargeIcon = 0x000000000;
    internal const uint ShgfiSmallIcon = 0x000000001;
    internal const uint ShgfiUseFileAttributes = 0x000000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShFileInfo
    {
        internal IntPtr hIcon;
        internal int iIcon;
        internal uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SHGetFileInfoW(
        string path, uint fileAttributes, ref ShFileInfo info, uint sizeOfInfo, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        // An int rather than a marshalled bool: LibraryImport refuses a struct that needs
        // runtime marshalling, and a BOOL is a 32-bit int anyway. Nothing here reads it.
        internal int fIcon;
        internal int xHotspot;
        internal int yHotspot;
        internal IntPtr hbmMask;
        internal IntPtr hbmColor;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetIconInfo(IntPtr icon, out IconInfo info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint biSize;
        internal int biWidth;
        internal int biHeight;
        internal ushort biPlanes;
        internal ushort biBitCount;
        internal uint biCompression;
        internal uint biSizeImage;
        internal int biXPelsPerMeter;
        internal int biYPelsPerMeter;
        internal uint biClrUsed;
        internal uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Bitmap
    {
        internal int bmType;
        internal int bmWidth;
        internal int bmHeight;
        internal int bmWidthBytes;
        internal ushort bmPlanes;
        internal ushort bmBitsPixel;
        internal IntPtr bmBits;
    }

    [LibraryImport("gdi32.dll")]
    internal static partial int GetObjectW(IntPtr handle, int size, ref Bitmap bitmap);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(
        IntPtr dc, IntPtr bitmap, uint startScan, uint scanLines,
        byte[]? bits, ref BitmapInfoHeader info, uint usage);
}
