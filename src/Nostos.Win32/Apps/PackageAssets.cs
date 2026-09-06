namespace Nostos.Win32.Apps;

/// <summary>
/// Finds the logo image shipped inside an MSIX package.
///
/// A packaged app has no icon anywhere the shell will hand over: its launcher under
/// <c>Program Files\WindowsApps</c> is a zero-length reparse point, so asking Windows for the
/// file's icon returns nothing and the generic-executable fallback returns a blank page. What it
/// does have is the artwork the Store shows, sitting in the package as ordinary PNG files.
///
/// <para>Two details make this less obvious than it sounds. The assets usually live in a
/// <em>different package</em> from the one the registry names -- a sibling ending
/// <c>_split.scale-100</c>, because Windows splits resources out by display scale -- and the
/// file names are chosen by the app's author, so there is no fixed name to look for. Calculator
/// ships <c>CalculatorAppList.scale-100.png</c>; another app ships
/// <c>Square44x44Logo.targetsize-32.png</c>.</para>
///
/// So the search is over the package and its siblings, and the ranking prefers the roles that
/// are meant to be small: the app-list icon first, then the 44px tile, then the Store logo.
/// </summary>
public static class PackageAssets
{
    private static readonly string WindowsApps = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

    /// <summary>
    /// Where a package keeps its artwork.
    ///
    /// "Assets" is the convention and what the templates generate, but it is only a convention:
    /// MSI Center on this machine uses "Images". The package root is included last because a few
    /// small packages put their logo beside the manifest and no deeper.
    /// </summary>
    private static readonly string[] AssetDirectories = ["Assets", "Images", "Resources", ""];

    /// <summary>The best logo file for a package, or null when it ships none this can find.</summary>
    /// <param name="packageFullName">e.g. Microsoft.WindowsCalculator_11.2502.2.0_x64__8wekyb3d8bbwe</param>
    /// <param name="packageRoot">
    /// The folder the package repository recorded for this package. Worth passing: packages can
    /// live on any volume -- an app moved to another drive is under that drive's own
    /// WindowsApps -- so the location is read rather than assumed to be under Program Files.
    /// </param>
    public static string? FindLogo(string packageFullName, string? packageRoot = null)
    {
        var candidates = new List<string>();

        foreach (var folder in PackageFolders(packageFullName, packageRoot))
        {
            foreach (var directory in AssetDirectories)
            {
                var assets = directory.Length == 0 ? folder : Path.Combine(folder, directory);
                if (!Directory.Exists(assets))
                    continue;

                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(assets, "*.png", SearchOption.TopDirectoryOnly));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A package this account cannot read simply has no icon, which is what the
                    // monogram in the window is for.
                }
            }
        }

        return candidates
            .Where(IsUsable)
            .OrderBy(Rank)
            // Largest within a role, because these ship at several target sizes and the row
            // draws at 26 pixels: a 48px asset scaled down looks right, a 16px one scaled up
            // looks like a mistake. Without this the 16px variant wins on file size alone.
            .ThenByDescending(f => Size(f))
            .FirstOrDefault();
    }

    /// <summary>
    /// The folders that could hold this package's artwork: its own, and the resource packages
    /// split out beside it.
    ///
    /// <para><b>Nothing here lists the WindowsApps directory, and that is the entire point.</b>
    /// Listing that folder needs administrator rights -- a plain user gets
    /// <c>UnauthorizedAccessException</c> from it -- while <em>reading inside</em> a package they
    /// already know the name of is allowed and always was. An earlier version searched for
    /// sibling packages by listing the root, so for anybody not running elevated the very first
    /// call threw and every packaged app in the list lost its icon at once. It looked correct in
    /// testing for the worst possible reason: the tests, and the app being watched, were both
    /// running elevated.</para>
    ///
    /// <para>So the names are constructed instead of discovered. A split resource package is
    /// named from the same identity, version and publisher hash as its parent, and Windows
    /// defines the set of scales, so every candidate can be written down and checked with
    /// <see cref="Directory.Exists"/> -- which a plain user is allowed to do.</para>
    /// </summary>
    internal static IEnumerable<string> PackageFolders(string packageFullName, string? packageRoot)
    {
        // The container is derived from where this package actually is, rather than assumed:
        // packages can sit on any volume, each with its own WindowsApps.
        var container = packageRoot is { Length: > 0 } && Path.GetDirectoryName(packageRoot) is { Length: > 0 } parent
            ? parent
            : WindowsApps;

        foreach (var name in CandidateFolderNames(packageFullName))
        {
            string folder;
            try
            {
                folder = Path.Combine(container, name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (Exists(folder))
                yield return folder;
        }
    }

    /// <summary>
    /// Every folder name a package's artwork could be under, most likely first.
    ///
    /// Split scale packages come first because that is where the artwork is when a package has
    /// them -- the main package is often left with little more than the executable. The scales
    /// are the fixed set Windows supports, so this is a complete list rather than a guess.
    ///
    /// <para>Pure string work, deliberately: it is the part worth testing, and it must not depend
    /// on what the account running it is allowed to see.</para>
    /// </summary>
    public static IReadOnlyList<string> CandidateFolderNames(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        if (parts.Length < 3)
            return [];

        var identity = parts[0];
        var version = parts[1];
        var publisher = parts[^1];

        if (identity.Length == 0 || version.Length == 0 || publisher.Length == 0)
            return [packageFullName];

        List<string> names = [];

        foreach (var scale in Scales)
            names.Add($"{identity}_{version}_neutral_split.scale-{scale}_{publisher}");

        names.Add(packageFullName);
        return names;
    }

    /// <summary>The display scales Windows defines a resource package for.</summary>
    private static readonly int[] Scales = [100, 125, 150, 200, 400];

    private static bool Exists(string folder)
    {
        try
        {
            return Directory.Exists(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// High-contrast variants are excluded.
    ///
    /// They exist for accessibility themes and are drawn as flat black-on-white or
    /// white-on-black silhouettes. Picked up by accident they look like a broken icon rather
    /// than an accessible one.
    /// </summary>
    public static bool IsUsable(string path)
        => !Path.GetFileName(path).Contains("contrast-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lower is better. The roles meant to be shown small come first.
    ///
    /// A large tile scaled down to 26 pixels is mostly padding: these images are laid out with
    /// their own margins, and the wide ones are branding banners rather than icons.
    /// </summary>
    public static int Rank(string path)
    {
        var name = Path.GetFileName(path);

        bool Has(string marker) => name.Contains(marker, StringComparison.OrdinalIgnoreCase);

        // A wide tile is a banner, never an icon.
        if (Has("Wide") || Has("SplashScreen") || Has("BadgeLogo"))
            return 90;

        if (Has("AppList")) return 0;
        if (Has("Square44x44")) return 1;
        if (Has("StoreLogo")) return 2;
        if (Has("Square150x150") || Has("MedTile")) return 3;
        if (Has("SmallTile") || Has("Square71x71")) return 4;
        if (Has("LargeTile") || Has("Square310x310")) return 6;

        return Has("Logo") || Has("Icon") ? 5 : 50;
    }
}
