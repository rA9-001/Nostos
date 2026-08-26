# Releasing

A release is a git tag. Everything else happens in
[`.github/workflows/release.yml`](../.github/workflows/release.yml).

```
git tag v0.3.0
git push origin v0.3.0
```

Nothing built locally is ever uploaded by hand. What people download is always traceable to a
commit and a workflow run, which is most of what the attestation is worth.

## One-time setup: the signing key

**Do this before the first release.** Until it is done, the release workflow refuses to publish
— deliberately, because an unsigned release is one that every installed copy will refuse to
install, which looks like a broken updater rather than a missing secret.

```powershell
dotnet run --project tools/Nostos.ReleaseTool -- keygen --out key.pem
gh secret set NOSTOS_SIGNING_KEY < key.pem
del key.pem
```

Then paste the printed public key into
[`src/Nostos.Core/Updates/ReleaseIntegrity.cs`](../src/Nostos.Core/Updates/ReleaseIntegrity.cs)
and commit it:

```csharp
public const string SigningPublicKeyBase64 = "MFkwEwYHKoZIzj0CAQ...";
```

That constant is what every build trusts. **Changing it later orphans everyone running an older
copy** — their application will refuse releases signed with the new key and they will have to
download once by hand. Back the private key up somewhere that survives a lost laptop.

### Signing offline, later

The key in a repository secret means whoever controls the GitHub account can sign a release. The
same tool signs without CI:

```powershell
gh release download v0.3.0 --pattern SHA256SUMS.txt
dotnet run --project tools/Nostos.ReleaseTool -- sign --checksums SHA256SUMS.txt --key-file key.pem
gh release upload v0.3.0 SHA256SUMS.txt.sig
```

Do that on a machine that is not the build machine, delete the secret, and taking over the
repository stops being enough to ship signed code. Worth doing once the project has users who are
not friends.

## What a release contains

| Asset | What it is |
| --- | --- |
| `Nostos.exe` | The whole application in one ahead-of-time compiled file. No installer, no runtime, no service. |
| `Nostos-<version>-win-x64.zip` | The folder build: app, CLI and the background service. |
| `SHA256SUMS.txt` | Hashes, in `sha256sum` format. |
| `SHA256SUMS.txt.sig` | ECDSA P-256 signature over that file's exact bytes. |

There is no MSI, and that is a decision rather than an omission. The application installs its own
service on first launch behind a single UAC prompt, so an installer would be a second thing that
claims to manage the same state; and an MSI would make updating worse, since an MSI can only be
upgraded by another elevated MSI install. A folder you can delete and a file you can double-click
are better for both.

## Version numbers

The tag is the version. `v0.3.0` produces binaries stamped `0.3.0`, and the workflow refuses any
tag that is not `vMAJOR.MINOR.PATCH` — the updater refuses to parse anything else, so a release
tagged `v0.3.0-beta` would be invisible to every installed copy.

`Directory.Build.props` keeps `0.1.0` for local builds. It is never the released number, and a
local build therefore always thinks an update is available. That is correct: it is not a release.

## Before tagging

- [ ] `dotnet test Nostos.slnx` passes. CI runs it again on the tag; the point of running it here
      is not re-tagging.
- [ ] `CHANGELOG.md` has a section for the version.
- [ ] Any new tweak has a docs page. CI enforces this, but finding out now is cheaper.
- [ ] If `ReleaseIntegrity.SigningPublicKeyBase64` changed, say so loudly in the release notes:
      everyone on an older build has to download by hand once.

## After it publishes

- [ ] Download `Nostos.exe` from the release page and run
      `gh attestation verify Nostos.exe --repo rA9-001/Nostos`.
- [ ] From a machine running the previous version, check that it offers the update and that
      installing it works. **This is the step that matters** — the updater is the one component
      whose failures only appear in the field, and a broken one cannot fix itself.
- [ ] Submit the binaries to
      [Microsoft's false-positive form](https://www.microsoft.com/en-us/wdsi/filesubmission) and
      VirusTotal before announcing. A Windows optimizer is exactly the shape of thing that gets
      flagged, and the turnaround is usually a day or two.

## If a release is wrong

Do not move the tag. Somebody has already downloaded it, and their copy will never see the
replacement because the version number did not change.

Tag `v0.3.1` and publish that. If the bad release is actively harmful, delete it from the
releases page as well — the updater reads `/releases/latest`, so deleting it moves everybody back
to the previous one.
