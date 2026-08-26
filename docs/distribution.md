# Distribution and code signing

This project ships unsigned for now, on purpose, and there is a plan to change that without
paying for a certificate. Both halves matter, because "edits HKLM, stops services, sets process
priorities, opens a LocalSystem control pipe" is also an accurate description of malware — this
project will be flagged, and the answer is transparency rather than evasion.

## What signing actually buys in 2026

Less than it used to. Microsoft removed EV certificates' automatic SmartScreen reputation, and
their documentation now states plainly that "paying a premium for EV solely to avoid SmartScreen
warnings is no longer justified."

| | Signed | Unsigned |
| --- | --- | --- |
| First-download SmartScreen warning | Yes | Yes |
| Publisher name in the dialog | Shown | "Unknown publisher" |
| Reputation carries to the next release | Yes, via the certificate | **No — every release restarts at zero** |
| Win11 Smart App Control | Passes | Blocked until reputation exists |
| Enterprise policy blocks | Bypassable | Often hard-blocked |

The third row is the one that hurts a frequently-updated tool. It is the reason to pursue
signing at all.

## The plan: SignPath Foundation

[SignPath Foundation](https://signpath.org/terms.html) issues genuine OV code-signing
certificates to open-source projects for free, with the key on their HSM and signing wired into
CI. Their conditions and what each means here:

| Condition | Status |
| --- | --- |
| OSI-approved licence, no dual-licensing | MIT ✅ |
| No proprietary components | none ✅ |
| Actively maintained | must be demonstrated over time |
| **Already released in the form to be signed** | cannot apply on day one |
| **No malware or potentially unwanted programs** | the real risk — see below |

That last one is the one to plan around. Microsoft explicitly warns that signing PUA-behaving
files gives the *certificate* negative reputation, so SignPath screens seriously, and a
"Windows optimizer" is exactly the shape of thing that gets PUA-classified.

The architecture is the defence, and it should be stated plainly in the application: no kernel
driver, no process injection, no bundled "cleaner" or "booster", no telemetry, no bundled
offers, every change journaled and revertible, every tweak documented with an honest evidence
rating.

**Sequence:** ship unsigned → accumulate a real release history and users → apply → sign
everything from then on with a stable identity.

## What to do in the meantime — permanently, not as a stopgap

**Package managers.** A manifest PR to [winget-pkgs](https://github.com/microsoft/winget-pkgs)
is free, and it is the natural channel for this audience. Scoop (`extras`) and Chocolatey too.
The hash is pinned in a manifest anyone can audit, and there is no browser Mark-of-the-Web
dialog.

**Do not hand the heuristics free ammunition.** This matters more than SmartScreen for this
category:

- **Ship no installer at all**, which is the decision taken here: a folder and a single file,
  both of which are more boring to a heuristic than any installer. If an installer ever becomes
  necessary, it must be a **WiX MSI** and not a bespoke `.exe` — a hand-rolled installer that
  writes files and registers a service is the exact shape being scanned for. See
  [releasing.md](releasing.md) for why there is not one.
- **No UPX, no packers, no obfuscation.** Instant flag, zero benefit on an open-source project.
- **No .NET single-file bundles.** They self-extract native libraries into a hidden folder
  under `%TEMP%` at startup and load them from there, which is the packed-dropper shape.
  `Directory.Build.props` sets `PublishSingleFile=false` with a comment pointing here.
- Keep startup code boring. The unusual behaviour belongs in the catalog, not in `Main`.

### The one-file build, and why it is allowed

`scripts\publish.ps1 -SingleFile` produces a single 27 MB executable, which sounds like exactly
the thing the previous point rules out. It is not, and the difference is worth being precise
about, because the reasoning is the same reasoning a malware analyst applies.

Ahead-of-time compilation puts every managed assembly *inside* the executable as native code.
Nothing is bundled, nothing is extracted, and there is no managed self-extracting host — the
file is an ordinary native Windows program, which is the most boring thing it could possibly be.

What ahead-of-time compilation cannot absorb is Skia, HarfBuzz and ANGLE, the C++ libraries
Avalonia renders through. Those travel zipped inside the executable and are written to disk on
first run. That is the part that resembles the pattern, so it is done in the open:

| | .NET single-file bundle | This build |
| --- | --- | --- |
| Where files land | `%TEMP%\.net\<app>\<hash>` | the app's own `data\runtime\<version>` |
| Visible to the user | No | Yes, next to the app |
| What is extracted | 18 MB of native libs, every first run | the same 18 MB, once per version |
| Managed code on disk | bundled, unpacked by a host | compiled into the exe, never on disk |

Extraction is atomic — unpack to a scratch directory, then one `Directory.Move` — so an
interrupted first run cannot leave a truncated DLL to be loaded next time. An installed copy
unpacks under `%LOCALAPPDATA%` rather than `%ProgramData%`: the directory is loaded from, and a
machine-wide directory that ordinary users can write to is a DLL-planting invitation.

The single-file build is the app only. There is no service in it, so it runs portable and never
asks for elevation on startup. See `src/Nostos.App/Startup/NativeAssets.cs`.

**Publish verifiable provenance.** `actions/attest-build-provenance` is free and lets anyone run
`gh attestation verify Nostos.exe --repo rA9-001/Nostos` to tie the binary to the
exact workflow run and commit. Put SHA-256 hashes in every release note. For this audience that
is *stronger* evidence than a certificate.

**File false-positive reports on every release, before announcing it.** VirusTotal, then
[Microsoft's Security Intelligence portal](https://www.microsoft.com/en-us/wdsi/filesubmission)
and the equivalent forms for whatever else flags it. Free, usually 1–3 days. It belongs in the
release checklist.

**Make building from source a first-class path.** `git clone && dotnet build` produces a binary
with no Mark-of-the-Web at all — no SmartScreen, no warning. For a tool that asks to run as
LocalSystem, "build it yourself and read what it does" is a legitimate primary channel.

**Set expectations in the README.** Screenshot the warning, explain why it appears, link the
hash and the attestation.

## What not to do

- **Self-signed certificates.** Microsoft's own table puts them in the same row as unsigned, and
  telling users to import a root certificate trains exactly the behaviour that gets people
  compromised.
- **Sigstore/cosign alone.** Good supply-chain hygiene, but Windows does not recognise it for
  Authenticode. Use it alongside, never instead.
- **Microsoft Store.** The only zero-warning path, but a LocalSystem service that rewrites HKLM
  will not survive Store certification.
- **"Just add an exclusion" or "disable Defender".** The fastest way to become the thing users
  are warned about.

## Smart App Control

On clean Windows 11 installs, Smart App Control is on by default and blocks unsigned
executables outright — not just downloaded ones, and with no "Run anyway". It disables itself on
machines with developer activity, so most tinkerers will not hit it, but some users will find
the app simply does not launch.

`nos doctor` reads
`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` and reports
the state, so this is a clear explanation instead of a mystery failure.

## Sources

- [SmartScreen reputation for Windows app developers — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- [SignPath Foundation conditions for open-source projects](https://signpath.org/terms.html)
- [Code signing options for Windows app developers — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
