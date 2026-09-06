namespace Nostos.Win32.Cleanup;

/// <summary>How a target is grouped in the window. Presentation only; nothing depends on it.</summary>
public enum CleanupGroup
{
    /// <summary>Scratch space Windows and installers write to and rarely clear.</summary>
    Temp,

    /// <summary>Compiled shaders. The reason this feature is worth having on a gaming machine.</summary>
    Shaders,

    /// <summary>Game launchers' web and asset caches.</summary>
    Launchers,

    /// <summary>Caches Windows itself keeps.</summary>
    Windows,

    /// <summary>Crash dumps and error reports.</summary>
    Diagnostics,
}

/// <summary>
/// One place that can be emptied, and the rules for emptying it.
/// </summary>
/// <param name="Path">
/// The folder. Never removed itself, only its contents: the program that owns it expects it to
/// exist, and several will not recreate it.
/// </param>
/// <param name="Pattern">
/// Which files inside it belong to this target. Not every cache folder holds only cache -- the
/// Explorer folder keeps the thumbnail databases alongside things that are not caches at all.
/// </param>
/// <param name="MinimumAge">
/// How old a file must be before it is touched. Only meaningful for scratch directories: a file
/// written into Temp a minute ago is very likely an installer's working directory, and deleting
/// it breaks whatever is mid-flight. Null means age does not matter, which is the right answer
/// for a shader cache -- the program that owns it holds it open or does not, and that is what
/// the in-use check is for.
/// </param>
/// <param name="NeedsElevation">
/// True for anything outside the user's own profile. Nostos never elevates itself, so these are
/// listed and measured either way and skipped with a reason when it is not running elevated.
/// </param>
public sealed record CleanupTarget(
    string Id,
    CleanupGroup Group,
    string Path,
    string Pattern = "*",
    TimeSpan? MinimumAge = null,
    bool NeedsElevation = false);

/// <summary>
/// Everywhere this program is willing to empty, and everywhere it refuses to.
///
/// <b>A fixed list of named places, never a search for folders that look like caches.</b> That
/// distinction is the whole safety argument, and it is not theoretical: on the machine this was
/// written on, <c>Steam\steamapps\downloading</c> held <b>172 GB</b> -- a paused update for a game,
/// sitting in a folder whose name and contents look exactly like disposable staging. A cleaner
/// that matched on shape rather than on knowledge would have destroyed it, and the person would
/// have found out by watching Steam download 172 GB again.
///
/// <para>The second rule follows from the first: <b>everything here regenerates.</b> A shader
/// cache is rebuilt on the next launch, a thumbnail database on the next folder view, a log on
/// the next run. That property is what makes deleting them outright correct rather than
/// reckless -- and it is why the Recycle Bin is not used here, unlike everywhere else in this
/// program. Recycling a cache frees nothing until the bin is emptied, and the bin is the undo
/// for the uninstaller. Anything that does not regenerate does not belong on this list.</para>
/// </summary>
public static class CleanupTargets
{
    /// <summary>Every target, whether or not it exists on this machine.</summary>
    public static IReadOnlyList<CleanupTarget> All { get; } = Build();

    private static IReadOnlyList<CleanupTarget> Build()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // A day, for scratch directories. Windows' own Storage Sense uses the same idea, and the
        // reason is the same: what is in Temp right now is probably in use by something running.
        var day = TimeSpan.FromHours(24);

        return
        [
            // ------------------------------------------------------------------ temp
            new("temp.user", CleanupGroup.Temp, Path.Combine(local, "Temp"), MinimumAge: day),
            new("temp.windows", CleanupGroup.Temp, Path.Combine(windows, "Temp"),
                MinimumAge: day, NeedsElevation: true),

            // ---------------------------------------------------------------- shaders
            //
            // The ones worth having. A shader cache grows without limit, and a corrupt one is a
            // real cause of stutter and visual glitches -- "clear the shader cache" is standard
            // advice that currently means finding these folders by hand.
            new("shaders.directx", CleanupGroup.Shaders, Path.Combine(local, "D3DSCache")),
            new("shaders.directx-store", CleanupGroup.Shaders,
                Path.Combine(local, "Microsoft", "DirectX Shader Cache")),
            new("shaders.nvidia-dx", CleanupGroup.Shaders, Path.Combine(local, "NVIDIA", "DXCache")),
            new("shaders.nvidia-gl", CleanupGroup.Shaders, Path.Combine(local, "NVIDIA", "GLCache")),
            new("shaders.nvidia-legacy", CleanupGroup.Shaders,
                Path.Combine(local, "NVIDIA Corporation", "NV_Cache")),
            new("shaders.amd", CleanupGroup.Shaders, Path.Combine(local, "AMD", "DxCache")),
            new("shaders.amd-dxc", CleanupGroup.Shaders, Path.Combine(local, "AMD", "DxcCache")),
            new("shaders.intel", CleanupGroup.Shaders, Path.Combine(local, "Intel", "ShaderCache")),

            // -------------------------------------------------------------- launchers
            new("launcher.steam-html", CleanupGroup.Launchers, Path.Combine(local, "Steam", "htmlcache")),
            new("launcher.epic-web", CleanupGroup.Launchers,
                Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache")),
            new("launcher.epic-logs", CleanupGroup.Launchers,
                Path.Combine(local, "EpicGamesLauncher", "Saved", "Logs")),
            new("launcher.battlenet", CleanupGroup.Launchers, Path.Combine(local, "Battle.net", "Cache")),
            new("launcher.discord", CleanupGroup.Launchers, Path.Combine(roaming, "discord", "Cache")),
            new("launcher.discord-gpu", CleanupGroup.Launchers, Path.Combine(roaming, "discord", "GPUCache")),

            // ---------------------------------------------------------------- windows
            new("windows.update-downloads", CleanupGroup.Windows,
                Path.Combine(windows, "SoftwareDistribution", "Download"), NeedsElevation: true),
            new("windows.delivery-optimization", CleanupGroup.Windows,
                Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local",
                    "Microsoft", "Windows", "DeliveryOptimization"),
                NeedsElevation: true),

            // Only the cache databases. The Explorer folder holds other state, and emptying the
            // whole thing is how a cleaner loses somebody's taskbar layout.
            new("windows.thumbnails", CleanupGroup.Windows,
                Path.Combine(local, "Microsoft", "Windows", "Explorer"), Pattern: "*cache_*.db"),

            // ------------------------------------------------------------ diagnostics
            new("diagnostics.crash-dumps", CleanupGroup.Diagnostics, Path.Combine(local, "CrashDumps")),
            new("diagnostics.wer-user", CleanupGroup.Diagnostics,
                Path.Combine(local, "Microsoft", "Windows", "WER")),
            new("diagnostics.wer-machine", CleanupGroup.Diagnostics,
                Path.Combine(programData, "Microsoft", "Windows", "WER"), NeedsElevation: true),
        ];
    }

    /// <summary>
    /// Places this will not empty, and the reason each one is refused.
    ///
    /// Written down rather than merely left out, because "we do not touch that" is only a
    /// promise if it is somewhere a person can read it -- and because a future change that adds
    /// one of these has to argue with the reason rather than simply not think of it. The test
    /// that holds this asserts these paths never appear in <see cref="All"/>.
    /// </summary>
    public static IReadOnlyList<(string Path, string Why)> NeverTouched { get; } =
    [
        (@"steamapps\downloading",
            "a paused or interrupted download. 172 GB of one on the machine this was written on, "
            + "in a folder that looks exactly like disposable staging."),
        (@"steamapps\workshop", "mod and workshop content somebody chose to install."),
        (@"%USERPROFILE%\Downloads",
            "files a person put there on purpose. Every cleaner that offers this is wrong."),
        (@"C:\Windows\Prefetch",
            "Windows uses it to start programs faster and rebuilds it slowly. Clearing it is a "
            + "cost with no benefit, and it is the most commonly recommended bad idea in this space."),
        (@"C:\Windows.old",
            "the rollback path to the previous Windows build. Windows removes it on its own "
            + "schedule; taking it early removes the way back."),
        ("pagefile.sys / hiberfil.sys",
            "not caches. Their size is a power and memory setting, and changing it belongs in a "
            + "tweak that says so, not in a sweep."),
        ("browser profiles",
            "cookies, sessions and saved passwords live beside the cache. The space is not worth "
            + "signing somebody out of everything."),
        ("the Recycle Bin",
            "it is the undo for this program's own removals. Emptying it as part of a sweep would "
            + "quietly destroy the way back from an uninstall."),
    ];
}
