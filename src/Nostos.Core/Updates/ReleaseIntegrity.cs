using System.Security.Cryptography;
using System.Text;

namespace Nostos.Core.Updates;

/// <summary>
/// Decides whether a downloaded file is the one the maintainer published.
///
/// This matters more here than it would in most applications. An update mechanism fetches code
/// and runs it, and in this program some of that code ends up running as LocalSystem. A
/// downloader that trusts whatever arrived is a way to turn one compromised release, one hijacked
/// account or one bad proxy into administrator on every machine that installed it.
///
/// <para><b>The chain.</b> A release publishes <c>SHA256SUMS.txt</c> listing every asset's hash,
/// and <c>SHA256SUMS.txt.sig</c>, an ECDSA P-256 signature over that file's exact bytes. The
/// public half is compiled into this program. To install an update, the signature must verify
/// against that key, and the asset's hash must match its line in the signed file. A hash on its
/// own would prove only that the download was not corrupted in transit, which TLS already
/// established; the signature is what ties the bytes to whoever holds the private key.</para>
///
/// <para><b>It fails closed.</b> No key configured, no signature file, a signature that does not
/// verify, or an asset missing from the list all refuse the update. An update mechanism that
/// degrades to "install it anyway" under any of those conditions has no security property at
/// all, because that is the state an attacker arranges.</para>
///
/// <para><b>What it does not defend against.</b> If the private key is stored in CI and the
/// repository account is taken over, the attacker can sign. Signing offline, on a machine that
/// is not the build machine, is what closes that; see docs/releasing.md. This is worth knowing
/// rather than glossing over, because the difference between the two is the difference between
/// "hard" and "merely inconvenient".</para>
/// </summary>
public static class ReleaseIntegrity
{
    /// <summary>
    /// Public half of the release signing key, as base64 SubjectPublicKeyInfo.
    ///
    /// Empty until a key is generated. While it is empty every update refuses to install, which
    /// is the correct behaviour rather than a bug: an unverifiable update is not safer than no
    /// update. Generate one with
    /// <c>dotnet run --project tools/Nostos.ReleaseTool -- keygen</c> and paste the public half
    /// here; see docs/releasing.md.
    ///
    /// Changing this value orphans everyone running an older build -- their copy will refuse
    /// releases signed with the new key and they will have to download once by hand. Treat it
    /// as permanent, and keep the private key somewhere that survives a lost laptop.
    /// </summary>
    public const string SigningPublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEb8iT+A+DxH9B5Dr7mAQ0AeNAUAqZ/IN3KpXnnAYjSdOvQxWE8ku/"
        + "BDferwiJXia2F4dX6B91FiMaXs3TJMZKIg==";

    public static bool IsSigningConfigured => SigningPublicKeyBase64.Length > 0;

    /// <summary>
    /// Verifies a detached signature over the checksum file.
    ///
    /// The signature covers the file's raw bytes, not a normalised or re-serialized form, so a
    /// checkout that rewrote its line endings would fail to verify. That is intentional: the
    /// thing being signed has to be the thing being read.
    /// </summary>
    public static bool VerifyChecksums(
        ReadOnlySpan<byte> checksumFile, ReadOnlySpan<byte> signature, string? publicKeyBase64 = null)
    {
        var key = publicKeyBase64 ?? SigningPublicKeyBase64;
        if (key.Length == 0 || signature.Length == 0)
            return false;

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);

            return ecdsa.VerifyData(
                checksumFile, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // A malformed key or signature is a failed verification, not a crash. Anything that
            // arrives over the network gets to be wrong without taking the process down.
            return false;
        }
    }

    /// <summary>
    /// Looks up one file's expected hash in a signed <c>SHA256SUMS.txt</c>.
    ///
    /// The format is the one `sha256sum` writes: a lowercase hex digest, whitespace, then the
    /// file name, optionally with a <c>*</c> marking binary mode. Names are compared
    /// case-insensitively because Windows file names are, and a lookup that missed on case
    /// would read as tampering.
    /// </summary>
    public static string? ExpectedHash(string checksumFile, string fileName)
    {
        foreach (var line in checksumFile.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var split = trimmed.IndexOf(' ');
            if (split <= 0)
                continue;

            var digest = trimmed[..split];
            var name = trimmed[(split + 1)..].TrimStart(' ', '*');

            if (digest.Length == 64
                && digest.All(Uri.IsHexDigit)
                && string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return digest.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the <c>.sig</c> file, which holds the signature as base64 text.
    ///
    /// Text rather than raw bytes so that the artefact is inspectable and survives being pasted
    /// into a release note, an issue or an email without a tool mangling it. Whitespace around
    /// it is tolerated because editors add it; anything else is a malformed signature, and a
    /// malformed signature is a failed verification rather than an exception.
    /// </summary>
    public static byte[] DecodeSignature(ReadOnlySpan<byte> signatureFile)
    {
        try
        {
            return Convert.FromBase64String(Encoding.ASCII.GetString(signatureFile).Trim());
        }
        catch (FormatException)
        {
            return [];
        }
    }

    /// <summary>SHA-256 of a stream, as lowercase hex. Streams rather than buffers: assets are megabytes.</summary>
    public static async Task<string> HashAsync(Stream content, CancellationToken ct = default)
    {
        var digest = await SHA256.HashDataAsync(content, ct).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison of two hex digests.
    ///
    /// Timing is not a realistic attack on a local file comparison, and using the fixed-time
    /// primitive costs nothing. The reason to bother is that this is the line where the decision
    /// gets made, and a plain <c>==</c> here invites someone later to decide a
    /// <c>StartsWith</c> would be fine.
    /// </summary>
    public static bool HashesMatch(string expected, string actual)
        => expected.Length == actual.Length
           && CryptographicOperations.FixedTimeEquals(
               Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
               Encoding.ASCII.GetBytes(actual.ToLowerInvariant()));
}
