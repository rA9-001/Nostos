using Nostos.Win32.Apps;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// The uninstaller, and the rules that make an automatic sweep defensible.
///
/// Finding what an installer left behind is name matching, and name matching is guesswork. The
/// only way to know for certain is to have watched the installation happen, which needs a
/// filesystem filter driver -- a kernel driver, which this program will not ship. So the
/// guessing is made as narrow as it can be, and everything it finds goes to the Recycle Bin
/// instead of being deleted.
///
/// These tests hold the narrowness and the survivability. They are the load-bearing part of the
/// feature: everything else is a list with icons on it.
/// </summary>
public sealed class UninstallerTests
{
    private static InstalledApp App(
        string name = "Contoso Widget",
        string? publisher = "Contoso",
        AppSource source = AppSource.Machine,
        string? uninstall = null,
        string? quiet = null)
        => new($"machine:{name}", name, source,
            Publisher: publisher, UninstallCommand: uninstall, QuietUninstallCommand: quiet);

    // ------------------------------------------------------------------ what may be recycled

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Windows\System32\drivers\etc")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\Program Files\WindowsApps")]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.Foo_1.0_x64__8wekyb3d8bbwe")]
    public void The_places_that_are_never_a_leftover_are_refused(string path)
    {
        // Being wrong about one of these costs the machine. The WindowsApps entries were found
        // by running the scan over a real machine and reading what it proposed: a Store package
        // has its own directory there, the deployment stack owns it, and moving it aside
        // corrupts state that nothing else can repair.
        Assert.True(RecycleBin.IsProtected(path), path);
    }

    [Theory]
    [InlineData(@"C:\Users\Someone\AppData\Local\Discord")]
    [InlineData(@"C:\Program Files\Contoso\Widget")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Some Game")]
    [InlineData(@"C:\ProgramData\Contoso")]
    public void An_ordinary_program_folder_is_not_refused(string path)
    {
        // The guard has to be narrow too. A rule that refused everything under Program Files
        // would make the sweep useless, which is its own kind of failure.
        Assert.False(RecycleBin.IsProtected(path), path);
    }

    [Fact]
    public void The_user_profile_itself_is_refused_but_a_folder_inside_it_is_not()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.True(RecycleBin.IsProtected(profile));
        Assert.False(RecycleBin.IsProtected(Path.Combine(profile, "SomeProgram")));
    }

    // ------------------------------------------------------------------ what is scanned at all

    [Theory]
    [InlineData(AppSource.Machine, true)]
    [InlineData(AppSource.Machine32, true)]
    [InlineData(AppSource.User, true)]
    [InlineData(AppSource.Store, false)]
    [InlineData(AppSource.Scoop, false)]
    [InlineData(AppSource.Chocolatey, false)]
    public void Only_Win32_installers_are_swept_up_after(AppSource source, bool scanned)
    {
        // MSIX packages are self-contained by design, so a scan over one finds no true leftovers
        // and two kinds of false one. On a real machine it proposed the package's own directory
        // under WindowsApps, and -- for the Store package called "DirectX" --
        // HKLM\SOFTWARE\Microsoft\DirectX, which is Windows' own key.
        Assert.Equal(scanned, Leftovers.IsWorthScanning(source));
        Assert.Equal(scanned, Leftovers.Scan(App(source: source)).Count > 0
                              || Leftovers.IsWorthScanning(source));
    }

    [Fact]
    public void A_package_is_never_scanned_even_when_it_would_have_matched()
    {
        Assert.Empty(Leftovers.Scan(App(source: AppSource.Store)));
    }

    // ------------------------------------------------------------------ how narrow the match is

    [Fact]
    public void A_candidate_folder_must_match_the_whole_name_never_a_prefix_of_it()
    {
        // The failure this prevents: uninstalling "Microsoft Teams" proposing
        // %LOCALAPPDATA%\Microsoft, which holds Edge, OneDrive and the Windows shell's own
        // state. Every candidate is a full path built from a full name, so a folder called
        // "Microsoft" can only ever be proposed for a program called "Microsoft".
        var candidates = Leftovers.FolderCandidates(App("Microsoft Teams", "Microsoft")).ToList();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.DoesNotContain(Path.Combine(local, "Microsoft"), candidates);
        Assert.Contains(Path.Combine(local, "Microsoft Teams"), candidates);
        Assert.Contains(Path.Combine(local, "MicrosoftTeams"), candidates);
    }

    [Fact]
    public void The_scan_never_walks_the_disk()
    {
        // Every candidate is under one of a fixed set of parents, or is the program's own
        // recorded install location. A scan that searched would eventually wander into a folder
        // that merely shares a word with the program.
        var app = App("Widget", "Contoso") with { InstallLocation = @"C:\Program Files\Contoso\Widget" };

        string[] allowed =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            @"C:\Program Files\Contoso\Widget",
        ];

        foreach (var candidate in Leftovers.FolderCandidates(app))
        {
            Assert.True(
                allowed.Any(a => candidate.StartsWith(a, StringComparison.OrdinalIgnoreCase)),
                $"{candidate} is outside the fixed set of places the scan may look");
        }
    }

    [Fact]
    public void The_registry_scan_is_a_fixed_list_and_not_a_search()
    {
        var keys = Leftovers.RegistryCandidates(App("Widget", "Contoso")).ToList();

        Assert.Contains(@"HKCU\Software\Widget", keys);
        Assert.Contains(@"HKCU\Software\Contoso\Widget", keys);
        Assert.Contains(@"HKLM\SOFTWARE\WOW6432Node\Contoso\Widget", keys);

        // The publisher's own key, which may hold settings for its other programs, is never a
        // candidate on its own.
        Assert.DoesNotContain(@"HKCU\Software\Contoso", keys);
        Assert.DoesNotContain(@"HKLM\SOFTWARE\Contoso", keys);
    }

    [Theory]
    [InlineData("Brave Browser 1.2.3", "Brave Browser")]
    [InlineData("Widget(R)", "Widget")]
    [InlineData("Widget™", "Widget")]
    [InlineData("Wootility 5.4.1", "Wootility")]
    [InlineData("Battle.net", "Battle.net")]
    public void A_display_name_is_trimmed_back_to_what_a_folder_would_be_called(string display, string expected)
        => Assert.Equal(expected, Leftovers.Clean(display));

    [Fact]
    public void A_name_too_short_to_be_distinctive_produces_no_candidates()
    {
        // "7z" or "R" as a folder name under %LOCALAPPDATA% would match far too much.
        Assert.Empty(Leftovers.NameVariants("R"));
        Assert.Empty(Leftovers.NameVariants("7z"));
        Assert.NotEmpty(Leftovers.NameVariants("Git"));
    }

    // ------------------------------------------------------------------ running the uninstaller

    [Theory]
    [InlineData("\"C:\\Program Files\\Thing\\unins000.exe\" /SILENT", @"C:\Program Files\Thing\unins000.exe", "/SILENT")]
    [InlineData("C:\\Program Files\\Thing\\unins000.exe /S", @"C:\Program Files\Thing\unins000.exe", "/S")]
    [InlineData("C:\\Thing\\unins.exe", @"C:\Thing\unins.exe", "")]
    public void An_uninstall_string_is_split_without_breaking_a_path_that_has_spaces_in_it(
        string command, string file, string arguments)
    {
        // Splitting on the first space turns "C:\Program Files\Thing\unins000.exe" into
        // "C:\Program" with an argument of "Files\Thing\unins000.exe", which starts nothing.
        var (actualFile, actualArguments) = AppRemoval.SplitCommand(command);

        Assert.Equal(file, actualFile);
        Assert.Equal(arguments, actualArguments);
    }

    [Fact]
    public void An_MSI_can_be_removed_unattended_even_with_no_quiet_string_of_its_own()
    {
        // Documented Windows Installer behaviour rather than a guess about somebody's setup.exe.
        // Guessing flags for an arbitrary installer is how you run an unattended *install*.
        var app = App(uninstall: "MsiExec.exe /I{2A3F1B44-8E7C-4C0A-9A21-1C1B4E3D5F60}");

        Assert.Equal("{2A3F1B44-8E7C-4C0A-9A21-1C1B4E3D5F60}", app.MsiProductCode);
        Assert.True(app.CanRemoveSilently);

        var (file, arguments, silent) = AppRemoval.CommandFor(app);

        Assert.Equal("msiexec.exe", file);
        Assert.Contains("/x {2A3F1B44-8E7C-4C0A-9A21-1C1B4E3D5F60}", arguments, StringComparison.Ordinal);
        Assert.Contains("/qn", arguments, StringComparison.Ordinal);
        Assert.True(silent);
    }

    [Fact]
    public void An_installer_with_no_silent_switch_is_run_visibly_rather_than_guessed_at()
    {
        var app = App(uninstall: @"C:\Program Files\Thing\unins000.exe");

        Assert.False(app.CanRemoveSilently);
        Assert.False(AppRemoval.CommandFor(app).Silent);
    }

    [Fact]
    public void A_program_that_registered_no_uninstaller_says_so_rather_than_running_something()
    {
        Assert.Null(AppRemoval.CommandFor(App()).File);
    }

    [Fact]
    public void A_backup_file_name_cannot_escape_the_backup_folder()
    {
        // The name comes from a registry key path, which is full of backslashes.
        var name = AppRemoval.Sanitise(@"HKLM\SOFTWARE\Contoso\Widget");

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain("..", name, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ reading the machine

    [Theory]
    [InlineData("Microsoft.AV1VideoExtension", "AV1 Video Extension")]
    [InlineData("Microsoft.WindowsCalculator", "Windows Calculator")]
    [InlineData("Contoso.Widget", "Widget")]
    public void A_package_with_an_unresolvable_name_still_gets_a_readable_one(string identity, string expected)
        => Assert.Equal(expected, InstalledApps.Humanise(identity));

    [Theory]
    [InlineData(@"C:\Program Files\Thing\app.exe,0", @"C:\Program Files\Thing\app.exe")]
    [InlineData(@"""C:\Program Files\Thing\app.exe"",-1", @"C:\Program Files\Thing\app.exe")]
    [InlineData(@"C:\Program Files\Thing\app.exe", @"C:\Program Files\Thing\app.exe")]
    public void An_icon_path_keeps_its_commas_and_loses_only_the_resource_index(string raw, string expected)
    {
        // A path may legitimately contain a comma, so only a trailing ",<digits>" is an index.
        Assert.Equal(expected, InstalledApps.IconFrom(raw));
    }

    [Theory]
    [InlineData("20240115", 2024, 1, 15)]
    public void An_install_date_is_read_when_it_is_there(string raw, int year, int month, int day)
        => Assert.Equal(new DateOnly(year, month, day), InstalledApps.ParseInstallDate(raw));

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("00000000")]
    [InlineData(null)]
    public void A_missing_or_broken_install_date_is_absent_rather_than_invented(string? raw)
        => Assert.Null(InstalledApps.ParseInstallDate(raw));

    [Theory]
    [InlineData("Microsoft.VCLibs.140.00_14.0_x64__8wekyb3d8bbwe", true)]
    [InlineData("Microsoft.NET.Native.Framework.2.2_2.2_x64__8wekyb3d8bbwe", true)]
    [InlineData("Microsoft.WindowsAppRuntime.1.5_5000.0_x64__8wekyb3d8bbwe", true)]
    [InlineData("Microsoft.WindowsCalculator_11.0_x64__8wekyb3d8bbwe", false)]
    [InlineData("9426MICRO-STARINTERNATION.MSICenter_2.0_x64__kzh8wxbdkxb8p", false)]
    public void Shared_runtimes_are_not_offered_as_things_to_uninstall(string fullName, bool framework)
    {
        // Several apps depend on one of these, and removing it breaks whatever still needs it.
        Assert.Equal(framework, InstalledApps.IsFramework(fullName));
    }

    // ------------------------------------------------------------------ package artwork

    [Theory]
    [InlineData("CalculatorAppList.scale-100.png", "CalculatorMedTile.scale-100.png")]
    [InlineData("Square44x44Logo.targetsize-32.png", "StoreLogo.png")]
    [InlineData("StoreLogo.png", "LargeTile.scale-100.png")]
    [InlineData("AppList.targetsize-16.png", "Wide310x150Logo.scale-100.png")]
    public void The_icon_shaped_artwork_is_preferred_over_the_banner_shaped_kind(string better, string worse)
    {
        // These are drawn at 26 pixels. A wide tile is a branding banner and a large tile is
        // mostly padding; the app-list icon is the one designed for a row.
        Assert.True(PackageAssets.Rank(better) < PackageAssets.Rank(worse), $"{better} vs {worse}");
    }

    [Fact]
    public void High_contrast_artwork_is_never_picked()
    {
        // Flat black-on-white silhouettes for accessibility themes. Picked up by accident they
        // look like a broken icon rather than an accessible one.
        Assert.False(PackageAssets.IsUsable("CalculatorAppList.scale-100_contrast-black.png"));
        Assert.False(PackageAssets.IsUsable("CalculatorAppList.scale-100_contrast-white.png"));
        Assert.True(PackageAssets.IsUsable("CalculatorAppList.scale-100.png"));
    }

    [Fact]
    public void A_packages_artwork_is_found_without_ever_listing_the_folder_it_lives_in()
    {
        // The regression test for the worst bug in this feature so far.
        //
        // Listing C:\Program Files\WindowsApps needs administrator rights: a plain user gets
        // UnauthorizedAccessException from the directory enumeration, though reading inside a
        // package whose name they already know is allowed and always was. The search used to
        // find sibling packages by listing that root, so for anybody not elevated the first call
        // threw and EVERY packaged app lost its icon at once.
        //
        // It survived review, a real-machine test and several screenshots because all of them
        // ran elevated. That is why this test builds its own package folders somewhere ordinary:
        // it exercises the search with no privileged path involved at all, so it fails if the
        // code ever goes back to discovering siblings rather than constructing their names.
        var root = Path.Combine(Path.GetTempPath(), $"nostos-packages-{Guid.NewGuid():N}");
        const string full = "Contoso.Widget_1.2.3.0_x64__abcdefghijklm";
        const string split = "Contoso.Widget_1.2.3.0_neutral_split.scale-100_abcdefghijklm";

        var main = Path.Combine(root, full);
        Directory.CreateDirectory(Path.Combine(main, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, split, "Assets"));

        try
        {
            // The main package keeps only a Store logo; the artwork is in the split package, as
            // it is for most of the Microsoft apps on a real machine.
            File.WriteAllBytes(Path.Combine(main, "Assets", "StoreLogo.png"), new byte[64]);
            File.WriteAllBytes(Path.Combine(root, split, "Assets", "AppList.targetsize-256.png"), new byte[256]);

            var logo = PackageAssets.FindLogo(full, main);

            Assert.NotNull(logo);
            Assert.Contains(split, logo, StringComparison.Ordinal);
            Assert.EndsWith("AppList.targetsize-256.png", logo, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_split_resource_packages_name_is_built_from_its_parents()
    {
        // Checked against a real folder name from this machine. A split package shares its
        // parent's identity, version and publisher hash and differs only in the scale, which is
        // what makes constructing the name possible instead of searching for it.
        var names = PackageAssets.CandidateFolderNames(
            "Microsoft.WindowsCalculator_11.2502.2.0_x64__8wekyb3d8bbwe");

        Assert.Contains(
            "Microsoft.WindowsCalculator_11.2502.2.0_neutral_split.scale-100_8wekyb3d8bbwe",
            names);

        // Every scale Windows defines, so a package that ships only a 200% asset is still found.
        foreach (var scale in new[] { 100, 125, 150, 200, 400 })
            Assert.Contains(names, n => n.Contains($"split.scale-{scale}_", StringComparison.Ordinal));

        // And the package's own folder, which is where the artwork is when there is no split
        // package at all -- Web Media Extensions and the other media extensions are like this.
        Assert.Contains("Microsoft.WindowsCalculator_11.2502.2.0_x64__8wekyb3d8bbwe", names);
    }

    [Fact]
    public void The_split_packages_are_looked_at_before_the_package_itself()
    {
        // Ordering, not preference: when a package has been split, the main one usually keeps
        // only a Store logo and the real artwork moved out.
        var names = PackageAssets.CandidateFolderNames(
            "Microsoft.WindowsCalculator_11.2502.2.0_x64__8wekyb3d8bbwe");

        Assert.EndsWith("Microsoft.WindowsCalculator_11.2502.2.0_x64__8wekyb3d8bbwe", names[^1]);
        Assert.Contains("split.scale-", names[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("Contoso.Widget_1.0.0.0")]
    public void A_name_that_is_not_a_package_identity_produces_no_candidates(string notAPackage)
        => Assert.Empty(PackageAssets.CandidateFolderNames(notAPackage));

    [Fact]
    public void A_packaged_app_can_actually_have_its_logo_found()
    {
        // Against the real machine, because the whole difficulty here is where Windows puts
        // things: the artwork usually lives in a sibling package ending _split.scale-100 rather
        // than in the one the registry names, and the file names are the app author's choice.
        //
        // Note what this cannot tell you. It passes whether or not the test host is elevated,
        // so it went on passing through a bug that blanked every one of these icons for a plain
        // user. A_packages_artwork_is_found_without_ever_listing_the_folder_it_lives_in is the
        // one that holds that; this one holds that the naming still matches reality.
        var packages = InstalledApps.List().Where(a => a.Source == AppSource.Store).ToList();
        if (packages.Count == 0)
            return;

        var found = packages.Count(
            a => PackageAssets.FindLogo(a.Id.Split(':', 2)[^1], a.InstallLocation) is not null);

        Assert.True(found > packages.Count / 2,
            $"only {found} of {packages.Count} packages produced a logo; the search has probably "
            + "stopped matching where Windows keeps them");
    }

    // ------------------------------------------------------------------ why a row is listed

    [Fact]
    public void The_registration_is_written_out_in_full_so_it_can_be_looked_at()
    {
        // "It came from the registry" explains nothing. A key path can be pasted into regedit,
        // which is exactly what somebody who does not believe a row is going to want to do.
        Assert.Equal(
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Riot Game teamfighttactics.live",
            AppDetails.Registration(new InstalledApp(
                "user:Riot Game teamfighttactics.live", "Teamfight Tactics", AppSource.User)));

        Assert.Equal(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Widget",
            AppDetails.Registration(new InstalledApp("machine32:Widget", "Widget", AppSource.Machine32)));

        Assert.Equal(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Widget",
            AppDetails.Registration(new InstalledApp("machine:Widget", "Widget", AppSource.Machine)));
    }

    [Fact]
    public void An_entry_whose_paths_have_all_gone_is_named_as_a_leftover_registration()
    {
        // The case somebody arrives confused about: a game removed months ago that is still in
        // the list, because what puts it there is a registry key rather than the program.
        var detail = AppDetails.For(App(
            uninstall: @"C:\Riot Games\Riot Client\gone.exe --uninstall-product=teamfighttactics")
            with { IconPath = @"C:\ProgramData\Riot Games\Metadata\gone\gone.ico" });

        Assert.True(detail.NothingLeft);
        Assert.Equal(detail.CheckedCount, detail.MissingCount);
        Assert.All(
            detail.Evidence.Where(e => e.Item != EvidenceItem.Registration),
            e => Assert.False(e.Present));
    }

    [Fact]
    public void An_entry_that_still_points_at_real_files_is_not_called_a_leftover()
    {
        var real = typeof(UninstallerTests).Assembly.Location;
        var detail = AppDetails.For(App(uninstall: Quoted(real) + " /S") with { IconPath = real });

        Assert.False(detail.NothingLeft);
        Assert.Equal(0, detail.MissingCount);
    }

    [Fact]
    public void A_partly_missing_entry_is_reported_as_partly_missing_rather_than_judged()
    {
        // Teamfight Tactics on the machine this was written on: the game's own files are gone,
        // but the launcher that would uninstall it is still installed. Neither "installed" nor
        // "leftover" is the truth, so the count is reported and the reader decides.
        var real = typeof(UninstallerTests).Assembly.Location;
        var detail = AppDetails.For(App(uninstall: Quoted(real) + " --uninstall-product=tft")
            with { IconPath = @"C:\ProgramData\Contoso\Metadata\gone.ico" });

        Assert.False(detail.NothingLeft);
        Assert.Equal(1, detail.MissingCount);
        Assert.Equal(2, detail.CheckedCount);
    }

    [Fact]
    public void An_uninstaller_named_without_a_path_is_not_reported_as_missing()
    {
        // "MsiExec.exe /X{...}" is the commonest uninstall string on any machine, and Windows
        // resolves it against the search path. Checking the bare name would resolve it against
        // the working directory instead and report fifteen rows on this machine as having a
        // missing uninstaller -- every one of them wrong, in the exact place somebody has come
        // to find out whether a row can be trusted.
        var detail = AppDetails.For(App(uninstall: @"MsiExec.exe /X{ad8a2fa1-06e7-4b0d-927d-6e54b3d31028}"));

        var uninstaller = detail.Evidence.Single(e => e.Item == EvidenceItem.Uninstaller);

        Assert.True(uninstaller.Present, uninstaller.Path);
        Assert.True(Path.IsPathRooted(uninstaller.Path), uninstaller.Path);
    }

    [Theory]
    [InlineData(AppSource.Machine, true)]
    [InlineData(AppSource.Machine32, true)]
    [InlineData(AppSource.User, true)]
    [InlineData(AppSource.Store, false)]
    [InlineData(AppSource.Scoop, false)]
    [InlineData(AppSource.Chocolatey, false)]
    public void Only_a_registry_entry_is_something_that_can_be_removed_on_its_own(
        AppSource source, bool canForget)
    {
        // A Store package, a scoop directory or a Chocolatey one is not a note about a program,
        // it is the program. "Remove the registration and leave the rest" would hide something
        // still installed, which is the opposite of what this list is for.
        Assert.Equal(canForget, AppDetails.CanForget(App(source: source)));
    }

    [Fact]
    public async Task Forgetting_something_that_is_not_a_registry_entry_is_refused()
    {
        var report = await AppRemoval.ForgetAsync(App(source: AppSource.Store));

        Assert.NotEmpty(report.Problems);
        Assert.Equal(0, report.KeysRemoved);
        Assert.Equal(0, report.FoldersRecycled);
        Assert.False(report.UninstallerRan);
    }

    [Fact]
    public async Task Forgetting_never_runs_an_uninstaller()
    {
        // The whole point of it. The entries this exists for either have no uninstaller left or
        // have one that shrugs because its program is already gone; running it is what the
        // ordinary Remove does, and it is what leaves these rows stuck.
        var report = await AppRemoval.ForgetAsync(App(
            source: AppSource.User,
            uninstall: @"C:\Contoso\gone\unins000.exe"));

        Assert.False(report.UninstallerRan);
        Assert.False(report.NeededTheUser);
    }

    [Theory]
    [InlineData(@"HKCU\SOFTWARE\Contoso", @"HKEY_CURRENT_USER\SOFTWARE\Contoso", true)]
    [InlineData(@"HKLM\SOFTWARE\Contoso", @"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso", true)]
    [InlineData(@"HKEY_CURRENT_USER\SOFTWARE\Contoso\", @"HKEY_CURRENT_USER\SOFTWARE\Contoso", true)]
    [InlineData(@"HKCU\SOFTWARE\Contoso", @"HKEY_CURRENT_USER\SOFTWARE\Fabrikam", false)]
    [InlineData(@"HKCU\SOFTWARE\Contoso", @"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso", false)]
    public void The_two_ways_of_spelling_a_hive_name_are_the_same_key(
        string left, string right, bool same)
    {
        // The leftover scan writes HKCU and the registration is written HKEY_CURRENT_USER,
        // because each is the natural form where it is produced and reg.exe takes both. Compared
        // as plain strings they differ, and the sweep would then remove the entry twice: it
        // would work, and then report a failure for the second attempt -- a row saying something
        // went wrong while the machine is in exactly the intended state.
        Assert.Equal(same, AppRemoval.IsSameKey(left, right));
    }

    // ------------------------------------------------------------------ one thing at a time

    [Fact]
    public async Task A_path_the_program_does_not_own_is_refused_before_anything_happens()
    {
        // The security boundary, and the reason removal takes an app id as well as a path.
        // Without this check a caller who can name any listed program could hand over any path
        // on the machine and have it recycled, or any registry key and have it deleted.
        var somebodyElses = Path.Combine(Path.GetTempPath(), $"nostos-not-ours-{Guid.NewGuid():N}");
        Directory.CreateDirectory(somebodyElses);

        try
        {
            var report = await AppRemoval.RemoveItemAsync(App(source: AppSource.User), somebodyElses);

            Assert.NotEmpty(report.Problems);
            Assert.Equal(0, report.FoldersRecycled);
            Assert.Equal(0, report.KeysRemoved);
            Assert.True(Directory.Exists(somebodyElses), "the folder was touched despite not being owned");
        }
        finally
        {
            Directory.Delete(somebodyElses, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows")]
    public async Task Naming_something_of_Windows_own_is_refused_like_anything_else_unowned(string path)
    {
        // Not a separate rule: these are refused because the program does not own them, which is
        // the same check that refuses a stranger's temp folder. The protected-path list is the
        // second line rather than the first.
        var report = await AppRemoval.RemoveItemAsync(App(source: AppSource.User), path);

        Assert.NotEmpty(report.Problems);
        Assert.Equal(0, report.FoldersRecycled);
        Assert.Equal(0, report.KeysRemoved);
    }

    [Fact]
    public void The_registration_is_one_of_the_things_that_can_be_taken_on_its_own()
    {
        // "Delete only the registry entry, leave the folder" is a real thing to want: a stale
        // row over a program whose files somebody would rather keep.
        var detail = AppDetails.For(App(source: AppSource.User));

        var registration = detail.Removable.SingleOrDefault(i => i.IsRegistration);

        Assert.NotNull(registration);
        Assert.Equal(RemovableKind.RegistryKey, registration.Kind);
        Assert.Equal(detail.Registration, registration.Path);
    }

    [Fact]
    public void The_registration_is_offered_last()
    {
        // Removing it takes the row out of the list, and with it the panel listing everything
        // else, so it is the bottom of the list rather than the top of it.
        var detail = AppDetails.For(App(source: AppSource.User));

        Assert.True(detail.Removable[^1].IsRegistration);
    }

    [Fact]
    public void A_store_package_offers_nothing_to_remove_piece_by_piece()
    {
        // There is no registration to take away and no leftover scan over a package, so the
        // panel offers nothing rather than offering something that would not work.
        var detail = AppDetails.For(App(source: AppSource.Store));

        Assert.DoesNotContain(detail.Removable, i => i.IsRegistration);
    }

    [Fact]
    public void An_icon_kept_outside_the_program_s_own_folder_can_be_cleared_up()
    {
        // The case the leftover scan cannot reach. Riot keeps a game's icon under
        // ProgramData\Riot Games\Metadata\<game>, which is the publisher's folder rather than
        // the program's, and the scan refuses substring matches for good reasons. It is still a
        // real leftover, and this is the only thing here that would offer to remove it.
        var folder = Path.Combine(Path.GetTempPath(), $"nostos-icons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var icon = Path.Combine(folder, "contoso.ico");

        try
        {
            File.WriteAllBytes(icon, new byte[128]);

            var detail = AppDetails.For(App(source: AppSource.User) with { IconPath = icon });

            var item = detail.Removable.SingleOrDefault(i => i.Kind == RemovableKind.File);
            Assert.NotNull(item);
            Assert.Equal(icon, item.Path);
            Assert.Equal(128, item.SizeBytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_icon_that_is_gone_is_not_offered_for_removal()
    {
        var detail = AppDetails.For(App(source: AppSource.User)
            with { IconPath = @"C:\ProgramData\Contoso\Metadata\gone.ico" });

        Assert.DoesNotContain(detail.Removable, i => i.Kind == RemovableKind.File);
    }

    // ------------------------------------------------------- what needs administrator rights

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Contoso")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso")]
    [InlineData(@"hkey_local_machine\software\contoso")]
    [InlineData(@"C:\Program Files\Contoso")]
    [InlineData(@"C:\ProgramData\Contoso\Metadata")]
    [InlineData(@"C:/ProgramData/Contoso/Metadata")]
    public void What_an_ordinary_account_cannot_remove_is_known_from_the_path(string path)
    {
        Assert.True(AppDetails.IsMachineWide(path));
    }

    [Theory]
    [InlineData(@"HKCU\SOFTWARE\Contoso")]
    [InlineData(@"HKEY_CURRENT_USER\SOFTWARE\Contoso")]
    [InlineData(@"C:\Users\Someone\AppData\Local\Contoso")]
    [InlineData(@"D:\Games\Contoso")]
    public void Anything_the_account_owns_is_not_claimed_to_need_rights(string path)
    {
        // The error worth avoiding is the other one. Claiming administrator rights are needed
        // when they are not teaches somebody to restart the program elevated out of habit,
        // which is the habit this program is built not to ask for.
        Assert.False(AppDetails.IsMachineWide(path));
    }

    [Fact]
    public void A_machine_registration_is_marked_before_the_button_is_pressed()
    {
        // Read with the rest of the panel, in the process that would do the removing, so the
        // row can say so rather than reg.exe saying "access is denied" without saying whose.
        var detail = AppDetails.For(App(source: AppSource.Machine), elevated: false);

        Assert.True(detail.ForgetNeedsAdmin);
        Assert.True(detail.Removable.Single(i => i.IsRegistration).NeedsAdmin);
    }

    [Fact]
    public void The_same_registration_is_unmarked_when_the_rights_are_there()
    {
        var detail = AppDetails.For(App(source: AppSource.Machine), elevated: true);

        Assert.False(detail.ForgetNeedsAdmin);
    }

    [Fact]
    public void A_per_user_registration_never_needs_them()
    {
        var detail = AppDetails.For(App(source: AppSource.User), elevated: false);

        Assert.False(detail.ForgetNeedsAdmin);
    }

    [Fact]
    public void The_uninstaller_is_never_offered_for_removal()
    {
        // Deleting it strands a program that is still installed with no supported way to remove
        // it, which is a worse position than the one somebody opened this panel to get out of.
        var real = typeof(UninstallerTests).Assembly.Location;

        var detail = AppDetails.For(App(source: AppSource.User, uninstall: Quoted(real) + " /S"));

        Assert.DoesNotContain(detail.Removable, i =>
            string.Equals(i.Path, real, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ installer artwork

    [Theory]
    [InlineData("{D5B1B157-0F47-4E06-9C9A-97BD8A3E6039}", "751B1B5D74F060E4C9A979DBA8E30693")]
    [InlineData("{ad8a2fa1-06e7-4b0d-927d-6e54b3d31028}", "1AF2A8DA7E60D0B429D7E6453B3D0182")]
    [InlineData("{0788B172-EA4C-4BB2-B6DE-CF425BDFA8A7}", "271B8870C4AE2BB46BEDFC24B5FD8A7A")]
    public void A_product_code_packs_to_the_key_the_installer_actually_files_it_under(
        string productCode, string packed)
    {
        // The three pairs are real: each was read off this machine's registry rather than worked
        // out from the layout. That matters because getting the transform wrong does not throw
        // and does not look wrong -- it produces a key that simply does not exist, which is
        // indistinguishable from a product having no icon, and the feature would quietly do
        // nothing at all.
        //
        // The first three fields of a GUID are stored little-endian, so each one reverses whole;
        // the last two are stored as they are, so those swap a byte at a time. Two of the three
        // are lower-case input against upper-case storage, which is the other easy mistake.
        Assert.Equal(packed, MsiProducts.Pack(productCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a guid")]
    [InlineData("InstallShield_{ad8a2fa1-06e7-4b0d-927d-6e54b3d31028}")]
    public void Something_that_is_not_a_product_code_packs_to_nothing(string? notAGuid)
        => Assert.Null(MsiProducts.Pack(notAGuid));

    [Fact]
    public void An_MSI_product_can_actually_have_its_cached_icon_found()
    {
        // Against the real machine. This is the source that was missing: an MSI keeps its
        // artwork as a property inside the package, so an MSI-installed program routinely has no
        // DisplayIcon, no InstallLocation and nothing on disk with its name on it -- Epic Games
        // Launcher has all three absent. What it does have is the icon Windows cached at install
        // time, which is where Programs and Features gets the picture it shows.
        var msi = InstalledApps.List().Where(a => a.MsiProductCode is not null).ToList();
        if (msi.Count == 0)
            return;

        var found = msi.Count(a => MsiProducts.FindIcon(a.MsiProductCode) is not null);

        Assert.True(found > 0,
            $"none of {msi.Count} MSI products produced a cached icon; the packed-key transform "
            + "or the ProductIcon location has probably stopped matching");
    }

    [Fact]
    public void A_cached_icon_that_has_been_cleaned_away_is_not_offered()
    {
        // The existence check is not a nicety. The shell answers for a file that is not there --
        // ask it about a deleted path and it hands back the blank-page icon for the extension --
        // so a stale entry would end the search on a picture of nothing, for a row that had a
        // real icon waiting two steps further down.
        Assert.Null(MsiProducts.FindIcon("{00000000-0000-0000-0000-000000000000}"));
    }

    // ------------------------------------------------------------------ the icon search

    [Fact]
    public void What_the_publisher_nominated_wins_over_anything_found_by_searching()
    {
        var nominated = typeof(UninstallerTests).Assembly.Location;
        var app = App(uninstall: Quoted(nominated) + " /uninstall") with { IconPath = nominated };

        var icon = AppIcons.For(app);

        Assert.Equal(nominated, icon?.Path);
        Assert.Equal(IconKind.Resource, icon?.Kind);
    }

    [Fact]
    public void A_nominated_icon_whose_file_has_gone_falls_through_instead_of_ending_the_search()
    {
        var real = typeof(UninstallerTests).Assembly.Location;
        var app = App(uninstall: Quoted(real) + " /uninstall")
            with { IconPath = @"C:\Program Files\Contoso\gone\widget.exe" };

        Assert.Equal(real, AppIcons.For(app)?.Path);
    }

    [Fact]
    public void An_installer_package_with_no_artwork_is_drawn_as_an_installer_package()
    {
        // Seven Visual C++ runtimes on this machine are in exactly this position: no
        // DisplayIcon, no cached product icon, no install location, and an uninstaller that
        // belongs to Windows. They are not hiding their artwork -- they ship none.
        //
        // So the row says the one true thing left to say about it, with the same picture
        // Programs and Features uses. Note what it is not: the icon of msiexec.exe, which would
        // amount to claiming that Windows' own uninstaller is this program.
        var icon = AppIcons.For(App(uninstall: @"MsiExec.exe /X{ad8a2fa1-06e7-4b0d-927d-6e54b3d31028}"));

        Assert.Equal(IconKind.FileType, icon?.Kind);
        Assert.Equal(".msi", icon?.Path);
    }

    [Fact]
    public void Something_that_is_not_an_installer_package_and_has_nothing_gets_no_icon()
    {
        // The monogram is still the end of the line for anything that is not a package. There is
        // no picture of "a program in general" worth drawing, and a wrong one is worse than
        // letters: the icon is what somebody scans the list by.
        Assert.Null(AppIcons.For(App(uninstall: @"C:\Program Files\Contoso\gone\unins000.exe")));
    }

    [Theory]
    [InlineData(@"MsiExec.exe /X{ad8a2fa1-06e7-4b0d-927d-6e54b3d31028}")]
    [InlineData(@"C:\Windows\System32\msiexec.exe /X{ad8a2fa1-06e7-4b0d-927d-6e54b3d31028}")]
    [InlineData(@"rundll32.exe advpack.dll,LaunchINFSection widget.inf,Uninstall")]
    public void An_uninstaller_belonging_to_Windows_is_never_used_as_a_program_icon(string command)
    {
        // Without this every MSI product on the machine comes back with the same Windows
        // Installer box: a dozen identical rows that say nothing about which is which. The rule
        // is not a list of executable names but where the file lives -- anything inside the
        // Windows directory is Windows' own program, and its icon is Windows' icon.
        Assert.Null(AppIcons.Uninstaller(App(uninstall: command)));
    }

    [Fact]
    public void A_program_that_ships_its_own_uninstaller_can_be_recognised_by_it()
    {
        var real = typeof(UninstallerTests).Assembly.Location;

        Assert.Equal(real, AppIcons.Uninstaller(App(uninstall: Quoted(real) + " /S")));
    }

    [Fact]
    public void An_uninstaller_that_is_no_longer_on_disk_is_not_offered()
        => Assert.Null(AppIcons.Uninstaller(App(uninstall: @"C:\Program Files\Contoso\gone\unins000.exe")));

    [Fact]
    public void The_biggest_executable_in_a_folder_is_the_one_taken()
    {
        // A 400 KB launcher over a 12 KB helper that happens to sort first: crash handlers and
        // updaters live in these folders too and rarely carry the program's artwork.
        var folder = Path.Combine(Path.GetTempPath(), $"nostos-icons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllBytes(Path.Combine(folder, "aaa-helper.exe"), new byte[64]);
            File.WriteAllBytes(Path.Combine(folder, "widget.exe"), new byte[4096]);

            Assert.Equal(Path.Combine(folder, "widget.exe"), AppIcons.BiggestExecutable(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Program Files\Contoso\no-such-folder")]
    public void A_folder_that_is_not_there_yields_nothing_rather_than_throwing(string? folder)
        => Assert.Null(AppIcons.BiggestExecutable(folder));

    [Fact]
    public void An_entry_that_names_artwork_that_exists_gets_it()
    {
        // Held against the real machine, but only where the machine settles the question. An
        // entry whose DisplayIcon points at a file that is actually there has artwork by
        // definition, and returning anything else means the first and strongest of the six
        // sources has broken.
        //
        // What this deliberately does not assert is a proportion. It used to -- seven programs
        // in ten had to find their own artwork -- and that is a fact about which programs happen
        // to be installed, not about this code. It held on a desktop, at 89 of 105, and failed
        // the build on a CI runner at 56 of 106: a runner has build tools, SDKs and
        // redistributables on it and almost nothing that ships an icon. A test that passes or
        // fails on what somebody installed last week is not testing this program.
        foreach (var app in InstalledApps.List())
        {
            if (app.IconPath is not { Length: > 0 } icon || !File.Exists(icon))
                continue;

            Assert.Equal(new AppIcon(icon, IconKind.Resource), AppIcons.For(app));
        }
    }

    [Fact]
    public void No_row_on_this_machine_is_left_with_nothing_to_draw()
    {
        // The weaker claim, and the one that is true anywhere: the search either finds artwork
        // or the row is an installer package, which is itself something to draw. A program that
        // is neither gets a monogram from the window, so nothing is ever blank -- but a *package*
        // falling through to that would mean the last fallback had stopped working.
        Assert.All(
            InstalledApps.List(),
            app => Assert.True(AppIcons.For(app) is not null || app.MsiProductCode is null));
    }

    private static string Quoted(string path) => '"' + path + '"';

    [Fact]
    public void The_machine_can_actually_be_enumerated()
    {
        // Not a mock. The whole point of the tab is that this list is real, and the filtering
        // rules that turn ~300 registry keys into the programs a person would recognise are
        // exactly the kind of thing that rots silently.
        var all = InstalledApps.List();

        Assert.NotEmpty(all);
        Assert.All(all, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
        Assert.All(all, a => Assert.False(string.IsNullOrWhiteSpace(a.Id)));

        // Ids have to be unique: a removal is aimed by id, and two rows sharing one would aim
        // at whichever happened to sort first.
        Assert.Equal(all.Count, all.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // And every id resolves back to the row it came from, which is the check the service
        // boundary relies on.
        var first = all[0];
        Assert.Equal(first.Id, InstalledApps.Find(first.Id)?.Id);
    }

    [Fact]
    public void No_resource_reference_ever_reaches_the_screen_as_a_name()
    {
        // A package stores its name as "ms-resource:AppStoreName" and the real text lives in its
        // compiled resource index. A row reading "ms-resource:AppName" is what the naive version
        // shows.
        foreach (var app in InstalledApps.List())
        {
            Assert.DoesNotContain("ms-resource", app.Name, StringComparison.OrdinalIgnoreCase);
            Assert.False(app.Name.StartsWith("@{", StringComparison.Ordinal), app.Name);
        }
    }
}
