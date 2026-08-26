using System.Diagnostics;
using System.IO.Compression;
using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Updates;

namespace Nostos.Win32.Updates;

/// <summary>Which shape of installation is being updated, which decides how.</summary>
public enum InstallKind
{
    /// <summary>One self-contained <c>Nostos.exe</c>. Replaced by swapping a single file.</summary>
    SingleFile,

    /// <summary>A folder with the service beside the app. Replaced wholesale.</summary>
    Folder,
}

/// <param name="Applied">False means nothing was changed. <paramref name="Message"/> says why.</param>
/// <param name="NeedsRelaunch">The new build is in place and the running one is stale.</param>
public sealed record UpdateOutcome(bool Applied, string Message, bool NeedsRelaunch = false);

/// <summary>
/// Puts a downloaded release in place.
///
/// <para><b>Replacing a running program.</b> Windows will not let you overwrite or delete an
/// executable that is running, but it will let you <em>rename</em> one -- the file's identity
/// moves and the mapped image goes on running from it. So the sequence is rename the current
/// build out of the way, move the new one in, relaunch, and delete the leftover on the next
/// start. This is the same trick every self-updating Windows program uses, and it is the reason
/// this can work without an installer or a helper service.</para>
///
/// <para><b>Nothing is written before everything is verified.</b> Download, check the signature
/// on the checksum file, check the asset's hash against it, and only then touch the installed
/// copy. The verification lives in <see cref="ReleaseIntegrity"/> and fails closed.</para>
/// </summary>
public sealed class UpdateInstaller
{
    /// <summary>Suffix for the displaced previous build. Cleaned up on the next launch.</summary>
    public const string DisplacedSuffix = ".old";

    private readonly ILogSink _log;

    public UpdateInstaller(ILogSink? log = null) => _log = log ?? NullLogSink.Instance;

    /// <summary>The directory the running build lives in.</summary>
    public static string InstallDirectory
        => Path.GetDirectoryName(Environment.ProcessPath!) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Whether this copy is one file or a folder.
    ///
    /// Decided by whether the service executable is beside the app rather than by a flag,
    /// because that is the thing that actually changes what has to happen: a folder with a
    /// service in it may have that service registered and running, and its files locked.
    /// </summary>
    public static InstallKind Kind
        => File.Exists(Path.Combine(InstallDirectory, "Nostos.Service.exe"))
            ? InstallKind.Folder
            : InstallKind.SingleFile;

    /// <summary>Scratch space for downloads, inside the app's own data folder.</summary>
    public static string StagingDirectory => Path.Combine(AppPaths.Root, "update");

    /// <summary>
    /// Removes the previous build left behind by an update.
    ///
    /// Called on startup. It cannot be done at the end of an update, because at that moment the
    /// file being deleted is the process doing the deleting.
    /// </summary>
    public static void CleanUpDisplacedBuild()
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(InstallDirectory, "*" + DisplacedSuffix))
            {
                try
                {
                    File.Delete(stale);
                }
                catch (IOException)
                {
                    // Still mapped, because the old process has not fully exited yet. It will
                    // go on the launch after this one; a stray file is not worth retrying over.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Downloads, verifies and installs a release.
    ///
    /// Every failure path leaves the installed copy exactly as it was. The only window where
    /// that is not true is between renaming the old build and moving the new one in, which is
    /// two file-system metadata operations with nothing between them.
    /// </summary>
    public async Task<UpdateOutcome> ApplyAsync(
        UpdateClient client,
        ReleaseInfo release,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!ReleaseIntegrity.IsSigningConfigured)
        {
            return new UpdateOutcome(false,
                "This build has no release signing key compiled into it, so it cannot verify an "
                + "update. Download the new version by hand from " + UpdateSource.ReleasesPage);
        }

        var wanted = Kind == InstallKind.SingleFile
            ? UpdateSource.PortableAsset
            : UpdateSource.FolderAsset(release.Version);

        if (release.Asset(wanted) is not { } asset)
            return new UpdateOutcome(false, $"Release {release.Tag} has no '{wanted}' to download.");

        if (release.Asset(UpdateSource.ChecksumAsset) is not { } sums
            || release.Asset(UpdateSource.SignatureAsset) is not { } signature)
        {
            return new UpdateOutcome(false,
                $"Release {release.Tag} is not signed: it has no {UpdateSource.ChecksumAsset} and "
                + $"{UpdateSource.SignatureAsset}. Refusing to install it.");
        }

        Directory.CreateDirectory(StagingDirectory);

        var checksumBytes = await client.DownloadBytesAsync(sums, ct).ConfigureAwait(false);
        var signatureBytes = await client.DownloadBytesAsync(signature, ct).ConfigureAwait(false);

        if (!ReleaseIntegrity.VerifyChecksums(checksumBytes, ReleaseIntegrity.DecodeSignature(signatureBytes)))
        {
            return new UpdateOutcome(false,
                "The release's checksum file is not signed by the key this build trusts. "
                + "Nothing was installed. If you changed signing keys, download by hand once.");
        }

        var expected = ReleaseIntegrity.ExpectedHash(
            System.Text.Encoding.UTF8.GetString(checksumBytes), asset.Name);

        if (expected is null)
            return new UpdateOutcome(false, $"'{asset.Name}' is not listed in the signed checksums.");

        var downloaded = await client.DownloadAsync(asset, StagingDirectory, progress, ct).ConfigureAwait(false);

        await using (var stream = File.OpenRead(downloaded))
        {
            var actual = await ReleaseIntegrity.HashAsync(stream, ct).ConfigureAwait(false);
            if (!ReleaseIntegrity.HashesMatch(expected, actual))
            {
                File.Delete(downloaded);
                return new UpdateOutcome(false,
                    $"'{asset.Name}' does not match its signed hash. Nothing was installed.");
            }
        }

        _log.Info($"update: verified {asset.Name} for {release.Tag}");

        return Kind == InstallKind.SingleFile
            ? SwapSingleFile(downloaded)
            : StageFolder(downloaded);
    }

    /// <summary>Rename the running exe aside, move the new one into its place.</summary>
    private UpdateOutcome SwapSingleFile(string downloaded)
    {
        var current = Environment.ProcessPath!;
        var displaced = current + DisplacedSuffix;

        try
        {
            if (File.Exists(displaced))
                File.Delete(displaced);

            // Renaming a running image is permitted; overwriting or deleting one is not.
            File.Move(current, displaced);

            try
            {
                File.Move(downloaded, current);
            }
            catch
            {
                // Put it back. A failure here would otherwise leave the machine with no
                // Nostos.exe at all, which is a far worse outcome than a failed update.
                File.Move(displaced, current);
                throw;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new UpdateOutcome(false,
                $"Could not replace {Path.GetFileName(current)}: {e.Message}. Nothing was changed.");
        }

        _log.Info($"update: replaced {current}");
        return new UpdateOutcome(true, "Updated. Restart to run the new version.", NeedsRelaunch: true);
    }

    /// <summary>
    /// Unpack the new folder, then hand the swap to an elevated helper.
    ///
    /// A folder install has a LocalSystem service running out of it, holding its own executable
    /// open, and only an administrator can stop it. So the unprivileged half stops at "the new
    /// files are unpacked and verified" and <c>Nostos.Service.exe apply-update</c> does the rest
    /// behind a single UAC prompt.
    /// </summary>
    private UpdateOutcome StageFolder(string archive)
    {
        var staged = Path.Combine(StagingDirectory, "staged");

        try
        {
            if (Directory.Exists(staged))
                Directory.Delete(staged, recursive: true);

            Extract(archive, staged);
            File.Delete(archive);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new UpdateOutcome(false, $"Could not unpack the update: {e.Message}");
        }

        var helper = Path.Combine(InstallDirectory, "Nostos.Service.exe");
        var start = new ProcessStartInfo
        {
            FileName = helper,
            // ShellExecute + runas is the only way to raise the prompt; the child needs its own
            // elevated token to stop the service and rewrite files under Program Files.
            UseShellExecute = true,
            Verb = "runas",
        };
        start.ArgumentList.Add("apply-update");
        start.ArgumentList.Add(staged);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return new UpdateOutcome(false, "Could not start the elevated updater.");

            process.WaitForExit();

            return process.ExitCode == 0
                ? new UpdateOutcome(true, "Updated. Restart to run the new version.", NeedsRelaunch: true)
                : new UpdateOutcome(false, $"The elevated updater failed (exit {process.ExitCode}).");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Declining the UAC prompt lands here. Not an error; a decision.
            return new UpdateOutcome(false, "Update cancelled: administrator approval is needed to replace the service.");
        }
    }

    /// <summary>
    /// Extracts an archive, refusing entries that try to escape the destination.
    ///
    /// Modern .NET checks this too. It is written out anyway because this is the line where a
    /// malicious archive would become arbitrary file write, and a reader should be able to see
    /// the check rather than take it on faith that the framework still does it.
    /// </summary>
    private static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            // Directory entries have an empty name and are created implicitly below.
            if (entry.Name.Length == 0)
                continue;

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry '{entry.FullName}' points outside the target folder.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
