namespace Nostos.Win32.Apps;

/// <summary>How a file has to be read to get pixels out of it.</summary>
public enum IconKind
{
    /// <summary>An icon resource inside an .ico, .exe or .dll.</summary>
    Resource,

    /// <summary>An ordinary image file: a package's PNG logo.</summary>
    Image,

    /// <summary>
    /// Not a file at all: the icon Windows draws for a kind of file, named by its extension.
    ///
    /// The last resort, and the only entry here that describes something other than the program
    /// itself. <see cref="AppIcons.For"/> explains when that is the honest answer.
    /// </summary>
    FileType,
}

/// <summary>A file that carries a program's artwork, and how to read it.</summary>
public sealed record AppIcon(string Path, IconKind Kind);

/// <summary>
/// Where a program's icon is, tried in order of how much the answer can be trusted.
///
/// Separate from the window on purpose: this is a search over the registry and the disk with
/// several fallbacks and a couple of rules that are easy to get subtly wrong, and none of it
/// needs a UI framework to decide. Keeping it here means it can be tested headless, and the
/// window is left with the one job it cannot delegate -- turning bytes into a bitmap.
///
/// <para>The order is the point. Each step is a weaker claim about what the program looks like
/// than the one above it, so the first hit wins and the search stops.</para>
/// </summary>
public static class AppIcons
{
    /// <summary>The best artwork for a program, or null when it ships none anywhere reachable.</summary>
    public static AppIcon? For(InstalledApp app)
    {
        // 1. What the publisher nominated in their uninstall key. An explicit answer.
        if (Existing(app.IconPath) is { } nominated)
            return new AppIcon(nominated, IconKind.Resource);

        // 2. What Windows Installer cached at install time. This is where an MSI's artwork
        //    lives, and an MSI product very often has nothing else: no DisplayIcon, no
        //    InstallLocation, nothing on disk with the program's name on it.
        if (MsiProducts.FindIcon(app.MsiProductCode) is { } product)
            return new AppIcon(product, IconKind.Resource);

        // 3. A packaged app's own logo, which is a PNG inside the package rather than an icon.
        if (app.Source == AppSource.Store
            && PackageAssets.FindLogo(app.Id.Split(':', 2)[^1], app.InstallLocation) is { } logo)
        {
            return new AppIcon(logo, IconKind.Image);
        }

        // 4. The program's own uninstaller, which is usually branded even when nothing else is.
        if (Uninstaller(app) is { } uninstaller)
            return new AppIcon(uninstaller, IconKind.Resource);

        // 5. Failing all of that, the biggest executable where it was installed -- what the
        //    shell would have drawn for the program's shortcut.
        if (Existing(BiggestExecutable(app.InstallLocation)) is { } executable)
            return new AppIcon(executable, IconKind.Resource);

        // 6. Nothing of the program's own exists anywhere. If it is an installer package, that is
        //    itself worth drawing: Windows has a picture for a package, and it is the one
        //    Programs and Features puts on these very rows. Eleven programs on this machine end
        //    up here -- seven Visual C++ runtimes, a couple of SDK components -- and they are not
        //    hiding their artwork, they genuinely ship none. Saying "installer package" is a true
        //    statement about the row, which is more than a blank square manages.
        return app.MsiProductCode is not null ? new AppIcon(".msi", IconKind.FileType) : null;
    }

    /// <summary>
    /// The program's uninstaller, when it is the program's own and not one of Windows'.
    ///
    /// The exclusion is the whole reason this is not one line. Most of what is left by the time
    /// the search gets here is an MSI, whose uninstaller is <c>msiexec.exe</c>, and accepting
    /// that would end the search early: a package with a perfectly good executable sitting in its
    /// install directory would be drawn as the Windows Installer instead of as itself.
    ///
    /// <para>The rule is not a list of executable names but where the file lives. Anything inside
    /// the Windows directory is Windows' own program and its icon is Windows' icon rather than
    /// the program's, which reaches msiexec, rundll32, cmd and powershell at once.</para>
    /// </summary>
    public static string? Uninstaller(InstalledApp app)
    {
        var (file, _, _) = AppRemoval.CommandFor(app);

        if (Existing(file) is not { } path)
            return null;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return windows is { Length: > 0 }
               && path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? null
            : path;
    }

    /// <summary>
    /// Something to take an icon from in the install directory.
    ///
    /// Picked by size so a 400 KB launcher is preferred over a 12 KB helper that happens to sort
    /// first: crash handlers and updaters live in these folders too and rarely carry the
    /// program's artwork.
    /// </summary>
    public static string? BiggestExecutable(string? directory)
    {
        if (directory is not { Length: > 0 } || !Directory.Exists(directory))
            return null;

        try
        {
            return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A path, if there is a file at the end of it.
    ///
    /// Checked rather than assumed, because the shell answers for a file that is not there: ask
    /// it about a deleted path and it returns the blank-page icon for the extension. That is
    /// worse than nothing, since it is not null and so ends the search on a row that had a real
    /// icon waiting two steps further down.
    /// </summary>
    private static string? Existing(string? path)
    {
        if (path is not { Length: > 0 })
            return null;

        try
        {
            return File.Exists(path) ? path : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
