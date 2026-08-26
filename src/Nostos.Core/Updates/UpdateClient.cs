using System.Net.Http.Headers;

namespace Nostos.Core.Updates;

/// <summary>Where releases come from. One place, so it cannot drift between the app and the CLI.</summary>
public static class UpdateSource
{
    public const string Owner = "rA9-001";
    public const string Repository = "Nostos";

    public static string LatestReleaseApi => $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repository}/releases";

    /// <summary>The single-file portable build. What most people will be running.</summary>
    public const string PortableAsset = "Nostos.exe";

    public const string ChecksumAsset = "SHA256SUMS.txt";
    public const string SignatureAsset = "SHA256SUMS.txt.sig";

    /// <summary>The full folder, including the service. Version is substituted in.</summary>
    public static string FolderAsset(Version version) => $"Nostos-{version.Major}.{version.Minor}.{version.Build}-win-x64.zip";
}

/// <summary>What an update check concluded.</summary>
public sealed record UpdateStatus
{
    public required Version Current { get; init; }
    public ReleaseInfo? Latest { get; init; }

    /// <summary>Null when the check succeeded. Set when it could not be completed.</summary>
    public string? Problem { get; init; }

    public bool UpdateAvailable
        => Latest is not null && ReleaseVersion.IsNewer(Current, Latest.Version);

    public static UpdateStatus Failed(Version current, string problem)
        => new() { Current = current, Problem = problem };
}

/// <summary>
/// Asks GitHub whether there is a newer release, and downloads one when asked.
///
/// Checking is deliberately separate from installing, and nothing here writes anything outside a
/// caller-supplied directory. Deciding to install is somebody else's job -- see
/// <c>Nostos.Win32.Updates.UpdateInstaller</c> -- because that half needs to stop a service and
/// replace a running executable, and this half should be usable without any of that.
/// </summary>
public sealed class UpdateClient : IDisposable
{
    /// <summary>
    /// Refuses to fetch an asset larger than this.
    ///
    /// The size in the API response is attacker-controlled text, so it is not what gets enforced;
    /// the read is capped as it happens. Without a cap, "download the update" is a request to
    /// fill the disk of anyone who clicks it. The full folder build is about 51 MB.
    /// </summary>
    public const long MaxAssetBytes = 300L * 1024 * 1024;

    private readonly HttpClient _http;

    public UpdateClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        // GitHub rejects requests with no User-Agent outright. The version is included because
        // it costs nothing and makes the request log legible if anything ever needs debugging.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Nostos", CurrentVersion().ToString(3)));
        }
    }

    /// <summary>The version of the running build, from the entry assembly.</summary>
    public static Version CurrentVersion()
        => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0), 0)
            : new Version(0, 0, 0, 0);

    public async Task<UpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateSource.LatestReleaseApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            // 404 is the normal answer for a repository that has never published a release, not
            // an error worth alarming anybody about.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateStatus { Current = current, Latest = null };

            if (!response.IsSuccessStatusCode)
                return UpdateStatus.Failed(current, $"GitHub answered {(int)response.StatusCode}.");

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var latest = ReleaseInfo.Parse(json);

            return latest is null
                ? UpdateStatus.Failed(current, "the latest release could not be read.")
                : new UpdateStatus { Current = current, Latest = latest };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Offline, DNS failure, captive portal, corporate proxy. A failed update check is
            // not a problem the user has to act on, so it is reported and not thrown.
            return UpdateStatus.Failed(current, "could not reach GitHub.");
        }
    }

    /// <summary>
    /// Downloads one asset into <paramref name="directory"/> and returns its path.
    ///
    /// The file name comes from the asset record rather than from the URL or from any header the
    /// server sends, and it is checked for path separators before use. A download that is
    /// allowed to choose its own path is a download that can write to Startup.
    /// </summary>
    public async Task<string> DownloadAsync(
        ReleaseAsset asset, string directory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var name = System.IO.Path.GetFileName(asset.Name);
        if (name.Length == 0 || name != asset.Name)
            throw new InvalidOperationException($"Refusing to download an asset named '{asset.Name}'.");

        Directory.CreateDirectory(directory);
        var destination = System.IO.Path.Combine(directory, name);

        using var response = await _http
            .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var expected = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > MaxAssetBytes)
            {
                target.Close();
                File.Delete(destination);
                throw new InvalidOperationException(
                    $"'{name}' is larger than the {MaxAssetBytes / 1024 / 1024} MB limit; refusing it.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

            if (expected is > 0)
                progress?.Report(Math.Min(1.0, (double)written / expected.Value));
        }

        return destination;
    }

    /// <summary>Fetches a small text asset directly, for the checksum file and its signature.</summary>
    public async Task<byte[]> DownloadBytesAsync(ReleaseAsset asset, CancellationToken ct = default)
    {
        if (asset.SizeBytes > 1024 * 1024)
            throw new InvalidOperationException($"'{asset.Name}' is far too large to be a checksum file.");

        return await _http.GetByteArrayAsync(asset.DownloadUrl, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
