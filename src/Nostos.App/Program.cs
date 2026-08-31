using System.Runtime.CompilerServices;
using Avalonia;
using Nostos.App.Startup;
using Nostos.Core;

namespace Nostos.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Portable mode has to be decided before anything touches AppPaths, which is why it is
        // the first thing that happens. A folder containing "portable.txt" keeps its journal,
        // profiles and logs beside the executable instead of in %ProgramData%, so the whole
        // thing can live on a USB stick and carry its record of changes with it.
#if SINGLE_FILE
        // A one-file build is portable by construction: there is no service executable next to
        // it to install, and asking for administrator rights to set up something that is not
        // there would be a prompt with nothing behind it.
        const bool singleFile = true;
#else
        const bool singleFile = false;
#endif

        var portable = singleFile
                       || args.Contains("--portable", StringComparer.OrdinalIgnoreCase)
                       || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

        if (portable)
            AppPaths.UsePortableRoot(Path.Combine(AppContext.BaseDirectory, "data"));

        // Has to happen before any Avalonia code runs, because the first thing Avalonia does is
        // load Skia. In a single-file build the renderer lives inside this executable and needs
        // unpacking; in a folder build this finds the DLLs already there and does nothing.
        NativeAssets.Ensure();

        Start(args);
    }

    // Kept separate and un-inlined so that no Avalonia type is touched -- and therefore no
    // native library is loaded -- until NativeAssets.Ensure has returned.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Referenced by name by the Avalonia XAML previewer tooling.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            // Detected rather than named. Wiring the three backends by hand (UseWin32, UseSkia,
            // UseHarfBuzz) was tried and measured at 2ms faster out of ~195ms, which is noise,
            // and it is a standing invitation to the failure it caused on the first attempt:
            // leaving out text shaping does not degrade, it throws before a window exists.
            .UsePlatformDetect()
            .LogToTrace();
}
