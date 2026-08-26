using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nostos.Core.Updates;

/// <param name="Name">File name as published, e.g. "Nostos.exe".</param>
/// <param name="DownloadUrl">Direct download. Always https and always on a GitHub host.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeBytes);

/// <summary>One published release, as the update check understands it.</summary>
public sealed record ReleaseInfo
{
    public required string Tag { get; init; }
    public required Version Version { get; init; }
    public required string HtmlUrl { get; init; }
    public required DateTimeOffset PublishedUtc { get; init; }

    /// <summary>Release notes, as markdown. Shown to the user before they agree to anything.</summary>
    public string Notes { get; init => field = value ?? ""; } = "";

    public IReadOnlyList<ReleaseAsset> Assets { get; init => field = value ?? []; } = [];

    public ReleaseAsset? Asset(string name)
        => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the GitHub releases API response.
    ///
    /// Separated from fetching on purpose: this is the part with the decisions in it, and a pure
    /// function over a string can be tested against a real captured response without a network,
    /// a stub HTTP handler, or a rate limit.
    ///
    /// Drafts and pre-releases are refused rather than skipped. The endpoint used is
    /// <c>/releases/latest</c>, which already excludes both, so seeing one here means the URL
    /// was changed or the response was not what it claimed to be -- and quietly offering a draft
    /// build as an update is the wrong response to either.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        var payload = JsonSerializer.Deserialize(json, UpdateJson.Default.GithubRelease);
        if (payload?.TagName is null)
            return null;

        if (payload.Draft || payload.Prerelease)
            return null;

        if (ReleaseVersion.Parse(payload.TagName) is not { } version)
            return null;

        return new ReleaseInfo
        {
            Tag = payload.TagName,
            Version = version,
            HtmlUrl = payload.HtmlUrl ?? "",
            PublishedUtc = payload.PublishedAt,
            Notes = payload.Body ?? "",
            Assets =
            [
                .. (payload.Assets ?? [])
                    .Where(a => a.Name is not null && IsAcceptableDownload(a.BrowserDownloadUrl))
                    .Select(a => new ReleaseAsset(a.Name!, a.BrowserDownloadUrl!, a.Size)),
            ],
        };
    }

    /// <summary>
    /// Whether a download URL is one this program is willing to fetch from.
    ///
    /// The response is JSON from the internet, and <c>browser_download_url</c> is just a string
    /// in it. Following wherever it points would mean a compromised or spoofed response could
    /// aim the downloader at any host -- so the host is checked here rather than trusted. The
    /// signature check later is the real defence; this closes the door before it is needed.
    /// </summary>
    private static bool IsAcceptableDownload(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Parsing and comparing the versions in release tags.</summary>
public static class ReleaseVersion
{
    /// <summary>
    /// Turns "v1.2.3" into a <see cref="Version"/>, or null if it is not one.
    ///
    /// Anything with a pre-release suffix -- "v1.2.3-beta", "v1.2.3-rc1" -- is refused rather
    /// than truncated to 1.2.3. Treating a release candidate as its final version would offer
    /// people an upgrade to something that is not finished, and treating it as newer than the
    /// finished build would then refuse to move them off it.
    /// </summary>
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        // Version.TryParse accepts "1.2.3.4" and "1.2"; both are fine. What it also accepts is
        // leading and trailing whitespace, so the guard below is on the shape, not the parse.
        return text.Length > 0 && text.All(c => char.IsAsciiDigit(c) || c == '.')
               && Version.TryParse(text, out var version)
            ? Normalise(version)
            : null;
    }

    /// <summary>
    /// Fills in the unspecified components with zero.
    ///
    /// <c>Version</c> leaves them as -1, and -1 compares below 0, so "1.2" would sort *after*
    /// "1.2.0" and an update from 1.2 to 1.2.0 would look like a downgrade.
    /// </summary>
    private static Version Normalise(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>True when <paramref name="candidate"/> is a version worth moving to.</summary>
    public static bool IsNewer(Version current, Version candidate) => candidate > current;
}

// ---------------------------------------------------------------- wire shapes

/// <summary>
/// The subset of GitHub's release JSON this program reads.
///
/// Deliberately partial. The real response carries an author, an uploader, reaction counts and
/// several dozen URLs; binding all of it would mean this type had to change every time GitHub
/// added a field. System.Text.Json ignores what it is not asked about.
/// </summary>
internal sealed record GithubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; init; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
    [JsonPropertyName("body")] public string? Body { get; init; }
    [JsonPropertyName("draft")] public bool Draft { get; init; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
    [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; init; }
    [JsonPropertyName("assets")] public List<GithubAsset>? Assets { get; init; }
}

internal sealed record GithubAsset
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GithubRelease))]
internal sealed partial class UpdateJson : JsonSerializerContext;
