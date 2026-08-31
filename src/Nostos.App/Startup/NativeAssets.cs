using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Nostos.Core;
using Nostos.Core.Localization;

namespace Nostos.App.Startup;

/// <summary>
/// Unpacks the native rendering libraries when the app ships as a single file.
///
/// Avalonia draws through Skia, HarfBuzz and ANGLE, which are C++ DLLs. An ahead-of-time build
/// can compile all of our managed code into the executable but it cannot absorb those, so a
/// one-file build carries them as a zip inside itself and puts them on disk once, on first run.
///
/// Two deliberate differences from the .NET single-file bundler, which does something similar:
///
///  * The files land in a folder the user can see and delete, not a hidden directory under
///    %TEMP%. A gaming tool that writes executable code into a temp folder and loads it is
///    indistinguishable from a dropper, and the whole distribution strategy for this project
///    rests on not looking like one. See docs/distribution.md.
///  * Extraction is atomic. A half-written DLL that survives to the next launch would be loaded
///    and crash the process somewhere unrelated, so the unpack goes to a scratch directory and
///    is moved into place in one step.
///
/// In an ordinary folder build the DLLs are already next to the executable, this finds them
/// there, and nothing is written at all.
/// </summary>
internal static class NativeAssets
{
    private const string ResourceName = "Nostos.native.zip";

    /// <summary>Probe file: if this is beside the executable, the build is not single-file.</summary>
    private const string Probe = "libSkiaSharp.dll";

    /// <summary>
    /// Makes the native libraries loadable. Must run before anything touches Avalonia.
    /// </summary>
    /// <returns>A message worth showing the user, or null when nothing needed doing.</returns>
    public static string? Ensure()
    {
        // Cached because the startup checklist reads it after the fact, on a different thread.
        Warning = EnsureCore();
        return Warning is null ? null : Strings.Get(Warning);
    }

    /// <summary>
    /// The string-table key for what went wrong during <see cref="Ensure"/>, or null.
    ///
    /// A key rather than the message, because this runs before Avalonia and therefore before
    /// anything has read which language the user chose -- it is the first thing that happens
    /// after the portable check, and the renderer has to be on disk before a window can exist
    /// to say anything in. The checklist that shows this reads it much later, by which time
    /// the language is known.
    /// </summary>
    public static string? Warning { get; private set; }

    /// <summary>The one argument the unpack failure needs. Null when there is no failure.</summary>
    public static string? WarningDetail { get; private set; }

    private static string? EnsureCore()
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, Probe)))
            return null;

        var assembly = Assembly.GetExecutingAssembly();
        using var packed = assembly.GetManifestResourceStream(ResourceName);
        if (packed is null)
        {
            // Neither beside the executable nor inside it. Rendering will fail, but say why
            // rather than letting Avalonia throw a DllNotFoundException at the user.
            WarningDetail = Probe;
            return "notice.renderer.missing";
        }

        try
        {
            var directory = Unpack(packed, assembly);
            Preload(directory);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            WarningDetail = e.Message;
            return "notice.renderer.unpackfailed";
        }
    }

    /// <summary>
    /// Returns the directory holding the unpacked libraries, extracting them if this version
    /// has not been unpacked before.
    /// </summary>
    private static string Unpack(Stream packed, Assembly assembly)
    {
        // Keyed by version so an upgrade never loads last version's DLLs, and so two builds can
        // coexist without fighting over the same directory.
        var version = assembly.GetName().Version?.ToString() ?? "0";
        var root = CacheRoot();
        var target = Path.Combine(root, version);

        if (File.Exists(Path.Combine(target, Probe)))
            return target;

        Directory.CreateDirectory(root);

        var scratch = Path.Combine(root, $".unpack-{Environment.ProcessId}");
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, recursive: true);

        using (var archive = new ZipArchive(packed, ZipArchiveMode.Read))
            archive.ExtractToDirectory(scratch);

        try
        {
            Directory.Move(scratch, target);
        }
        catch (IOException)
        {
            // Another instance of the app won the race and moved its copy into place first.
            // Theirs is as good as ours; drop ours and use it.
            Directory.Delete(scratch, recursive: true);
        }

        CleanUpOldVersions(root, keep: version);
        return target;
    }

    /// <summary>
    /// Loads each library by full path.
    ///
    /// Windows resolves a later <c>DllImport("libSkiaSharp")</c> to a module of that name that
    /// is already loaded, so this is enough on its own. The search-path call is there for the
    /// libraries Avalonia loads by name itself rather than through P/Invoke.
    /// </summary>
    private static void Preload(string directory)
    {
        AddDllDirectory(directory);
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);

        foreach (var library in Directory.EnumerateFiles(directory, "*.dll"))
        {
            // A failure here is not fatal on its own: a library this build does not actually
            // use should not stop the app from starting.
            NativeLibrary.TryLoad(library, out _);
        }
    }

    /// <summary>
    /// Where unpacked libraries live.
    ///
    /// Portable mode keeps them inside the app's own data folder, because the promise there is
    /// that the whole thing travels together and leaves nothing behind. An installed copy uses
    /// per-user local state instead of %ProgramData%: this directory is loaded from, and a
    /// machine-wide directory that ordinary users can write to is a DLL-planting invitation.
    /// </summary>
    private static string CacheRoot()
        => AppPaths.IsPortable
            ? Path.Combine(AppPaths.Root, "runtime")
            : Path.Combine(AppPaths.LocalStateRoot, "runtime");

    private static void CleanUpOldVersions(string root, string keep)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(Path.GetFileName(directory), keep, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still in use by an older instance that is running. It will go next time.
            }
        }
    }

    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
    private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
