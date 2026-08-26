using System.Security.Cryptography;
using System.Text;
using Nostos.Core.Updates;

namespace Nostos.Core.Tests;

/// <summary>
/// The update check's parsing and version rules.
///
/// Every input here arrived over the internet, so the rule under test is usually "refuses"
/// rather than "accepts".
/// </summary>
public sealed class ReleaseParsingTests
{
    private const string Release = """
    {
      "tag_name": "v0.3.0",
      "html_url": "https://github.com/rA9-001/Nostos/releases/tag/v0.3.0",
      "body": "Four new ping tweaks.",
      "draft": false,
      "prerelease": false,
      "published_at": "2026-08-26T10:00:00Z",
      "assets": [
        { "name": "Nostos.exe", "browser_download_url": "https://github.com/rA9-001/Nostos/releases/download/v0.3.0/Nostos.exe", "size": 29000000 },
        { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/rA9-001/Nostos/releases/download/v0.3.0/SHA256SUMS.txt", "size": 200 }
      ]
    }
    """;

    [Fact]
    public void A_normal_release_is_read()
    {
        var release = ReleaseInfo.Parse(Release);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 3, 0, 0), release.Version);
        Assert.Equal("v0.3.0", release.Tag);
        Assert.Equal(2, release.Assets.Count);
        Assert.NotNull(release.Asset("nostos.exe"));
    }

    [Fact]
    public void A_draft_or_prerelease_is_refused_rather_than_offered()
    {
        // /releases/latest already excludes both, so one showing up here means the response was
        // not what it claimed. Quietly offering a draft build as an update is the wrong answer.
        Assert.Null(ReleaseInfo.Parse(Release.Replace("\"draft\": false", "\"draft\": true")));
        Assert.Null(ReleaseInfo.Parse(Release.Replace("\"prerelease\": false", "\"prerelease\": true")));
    }

    [Fact]
    public void An_asset_hosted_somewhere_other_than_github_is_dropped()
    {
        // browser_download_url is a string in a JSON document from the internet. Following it
        // anywhere it points would let a spoofed response aim the downloader at any host.
        var tampered = Release.Replace(
            "https://github.com/rA9-001/Nostos/releases/download/v0.3.0/Nostos.exe",
            "https://example.invalid/Nostos.exe");

        var release = ReleaseInfo.Parse(tampered);

        Assert.NotNull(release);
        Assert.Null(release.Asset("Nostos.exe"));
        Assert.Single(release.Assets);
    }

    [Fact]
    public void A_plain_http_asset_is_dropped()
    {
        var release = ReleaseInfo.Parse(Release.Replace("https://github.com/rA9-001/Nostos/releases/download/v0.3.0/Nostos.exe",
                                                        "http://github.com/rA9-001/Nostos/releases/download/v0.3.0/Nostos.exe"));

        Assert.NotNull(release);
        Assert.Null(release.Asset("Nostos.exe"));
    }

    [Fact]
    public void Rubbish_does_not_throw()
        => Assert.Null(ReleaseInfo.Parse("{ \"message\": \"Not Found\" }"));

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V0.1.0", 0, 1, 0)]
    public void Tags_with_and_without_a_v_both_parse(string tag, int major, int minor, int build)
        => Assert.Equal(new Version(major, minor, build, 0), ReleaseVersion.Parse(tag));

    [Theory]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1.2.3-rc1")]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_a_plain_version_is_refused(string? tag)
        => Assert.Null(ReleaseVersion.Parse(tag));

    [Fact]
    public void A_two_part_version_does_not_read_as_older_than_its_three_part_self()
    {
        // Version leaves unspecified components at -1, which sorts below 0, so without
        // normalising, 1.2 would compare as older than 1.2.0 and the two would chase each other.
        Assert.Equal(ReleaseVersion.Parse("v1.2"), ReleaseVersion.Parse("v1.2.0"));
        Assert.False(ReleaseVersion.IsNewer(ReleaseVersion.Parse("v1.2.0")!, ReleaseVersion.Parse("v1.2")!));
    }

    [Fact]
    public void Only_a_higher_version_counts_as_an_update()
    {
        var current = new Version(0, 2, 0, 0);

        Assert.True(ReleaseVersion.IsNewer(current, new Version(0, 2, 1, 0)));
        Assert.True(ReleaseVersion.IsNewer(current, new Version(1, 0, 0, 0)));
        Assert.False(ReleaseVersion.IsNewer(current, new Version(0, 2, 0, 0)));
        Assert.False(ReleaseVersion.IsNewer(current, new Version(0, 1, 9, 0)));
    }
}

/// <summary>
/// The signature and hash chain that decides whether downloaded code gets to run.
///
/// This is the highest-consequence code in the project: what it approves can end up running as
/// LocalSystem. Every test here is a refusal except the first.
/// </summary>
public sealed class ReleaseIntegrityTests
{
    private static (string PublicKey, ECDsa Key) NewKey()
    {
        var key = ECDsa.Create(ECCurve.CreateFromFriendlyName("nistP256"));
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static byte[] Sign(ECDsa key, byte[] data)
        => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    private const string Sums = """
        e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  Nostos.exe
        5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03 *Nostos-0.3.0-win-x64.zip
        """;

    [Fact]
    public void The_key_compiled_into_this_build_is_a_usable_P256_public_key()
    {
        // Guards the paste. The public key is copied by hand out of the release tool into a
        // source constant, and a truncated or mistyped one would build, ship, and then refuse
        // every update in the field with a message about the release being unsigned. Failing
        // here instead costs a second.
        Assert.True(ReleaseIntegrity.IsSigningConfigured);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(
            Convert.FromBase64String(ReleaseIntegrity.SigningPublicKeyBase64), out var read);

        Assert.Equal(256, ecdsa.KeySize);
        Assert.Equal(Convert.FromBase64String(ReleaseIntegrity.SigningPublicKeyBase64).Length, read);
    }

    [Fact]
    public void A_signature_made_by_the_matching_key_verifies()
    {
        var (publicKey, key) = NewKey();
        using var _ = key;
        var bytes = Encoding.UTF8.GetBytes(Sums);

        Assert.True(ReleaseIntegrity.VerifyChecksums(bytes, Sign(key, bytes), publicKey));
    }

    [Fact]
    public void A_signature_from_a_different_key_is_refused()
    {
        // The whole point. An attacker who can publish a release but does not hold the private
        // key can produce a perfectly well-formed signature, and it must not verify.
        var (publicKey, key) = NewKey();
        var (_, attacker) = NewKey();
        using var _ = key;
        using var __ = attacker;

        var bytes = Encoding.UTF8.GetBytes(Sums);

        Assert.False(ReleaseIntegrity.VerifyChecksums(bytes, Sign(attacker, bytes), publicKey));
    }

    [Fact]
    public void Editing_one_byte_of_the_checksum_file_breaks_the_signature()
    {
        var (publicKey, key) = NewKey();
        using var _ = key;

        var original = Encoding.UTF8.GetBytes(Sums);
        var signature = Sign(key, original);
        var tampered = Encoding.UTF8.GetBytes(Sums.Replace("e3b0c44298", "e3b0c44299"));

        Assert.False(ReleaseIntegrity.VerifyChecksums(tampered, signature, publicKey));
    }

    [Fact]
    public void With_no_key_configured_nothing_verifies()
    {
        // The shipped state until a key is generated. It must refuse, not wave things through:
        // an unverifiable update is not safer than no update.
        var (_, key) = NewKey();
        using var _ = key;
        var bytes = Encoding.UTF8.GetBytes(Sums);

        Assert.False(ReleaseIntegrity.VerifyChecksums(bytes, Sign(key, bytes), publicKeyBase64: ""));
    }

    [Fact]
    public void An_empty_or_malformed_signature_is_refused_and_does_not_throw()
    {
        var (publicKey, key) = NewKey();
        using var _ = key;
        var bytes = Encoding.UTF8.GetBytes(Sums);

        Assert.False(ReleaseIntegrity.VerifyChecksums(bytes, [], publicKey));
        Assert.False(ReleaseIntegrity.VerifyChecksums(bytes, [1, 2, 3], publicKey));
        Assert.False(ReleaseIntegrity.VerifyChecksums(bytes, Sign(key, bytes), "not base64 at all"));
    }

    [Fact]
    public void The_signature_file_round_trips_through_base64()
    {
        var (publicKey, key) = NewKey();
        using var _ = key;
        var bytes = Encoding.UTF8.GetBytes(Sums);

        // What the release tool writes: base64 text, and editors add a trailing newline.
        var file = Encoding.ASCII.GetBytes(Convert.ToBase64String(Sign(key, bytes)) + "\r\n");

        Assert.True(ReleaseIntegrity.VerifyChecksums(bytes, ReleaseIntegrity.DecodeSignature(file), publicKey));
    }

    [Fact]
    public void A_signature_file_that_is_not_base64_decodes_to_nothing_rather_than_throwing()
        => Assert.Empty(ReleaseIntegrity.DecodeSignature("<html>404 Not Found</html>"u8));

    [Theory]
    [InlineData("Nostos.exe", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("nostos.EXE", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("Nostos-0.3.0-win-x64.zip", "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03")]
    public void A_hash_is_found_by_name_including_the_binary_star_form(string name, string expected)
        => Assert.Equal(expected, ReleaseIntegrity.ExpectedHash(Sums, name));

    [Fact]
    public void An_asset_that_is_not_listed_has_no_hash()
    {
        // Which means it cannot be installed. A file present in a release but absent from the
        // signed list is exactly what an added payload would look like.
        Assert.Null(ReleaseIntegrity.ExpectedHash(Sums, "extra-tool.exe"));
    }

    [Fact]
    public void Hash_comparison_is_case_insensitive_but_length_strict()
    {
        const string digest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        Assert.True(ReleaseIntegrity.HashesMatch(digest, digest.ToUpperInvariant()));
        Assert.False(ReleaseIntegrity.HashesMatch(digest, digest[..63]));
        Assert.False(ReleaseIntegrity.HashesMatch(digest, digest[..10]));
    }
}
