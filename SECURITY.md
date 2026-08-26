# Security

This program edits `HKLM`, changes service start types, and runs a background service as
LocalSystem that accepts commands over a named pipe. That is an accurate description of the tool
and also an accurate description of a great deal of malware, so the security properties are
written down here rather than left to be inferred.

## Reporting a vulnerability

Use **[GitHub's private vulnerability reporting](https://github.com/rA9-001/Nostos/security/advisories/new)**.
It is on for this repository, so a report is visible only to the maintainers until it is fixed.

Please do not open a public issue for anything that would let one user of a machine gain
privilege over another, or let a remote party reach the control pipe.

There is no bounty. Expect a first response within a week; this is a hobby project maintained by
one person, and pretending otherwise would be worse than saying so.

## The parts worth attacking

If you are looking for somewhere to start, these are the places where a mistake would matter:

| Where | Why |
| --- | --- |
| `src/Nostos.Service/Daemon/ControlPipeServer.cs` | An unprivileged caller talking to a LocalSystem process. Requests are size-capped and the pipe's DACL is built from an explicit SID list. |
| `src/Nostos.Win32/ServiceControl/ServiceConfiguration.cs` | Builds that DACL. A broad group here would be a local privilege escalation. |
| `src/Nostos.Core/Updates/ReleaseIntegrity.cs` | Decides whether downloaded code is installed. |
| `src/Nostos.Win32/Updates/UpdateInstaller.cs` | Unpacks an archive and replaces executables. |
| `src/Nostos.Service/Program.cs`, `apply-update` | Runs elevated and overwrites an installation directory. |

## Deliberate properties

These are design commitments, not implementation details. A change that breaks one is a bug even
if everything still works.

**Nothing touches a running process.** No kernel driver, no DLL injection, no API hooking, no
reading or writing another process's memory. Every change goes through documented Win32 APIs from
outside. This is why the program is safe to run alongside anti-cheat software, and it is not
negotiable for a performance gain.

**The control pipe is never opened to a broad group.** Not `Users`, not `Authenticated Users`,
not `Everyone`. The DACL is built from the SIDs recorded at install time — the account that
installed it, plus SYSTEM and Administrators. Anything else would hand every account on the
machine a way to rewrite `HKLM`.

**The app never elevates itself.** `src/Nostos.App/app.manifest` is `asInvoker` and stays that
way. Elevation happens in exactly two places, both explicit and both visible as a UAC prompt:
installing the service, and applying an update to a folder install.

**The service does not watch what you run.** It never enumerates processes and never reacts to a
game starting. A background LocalSystem process that watches your programs is hard to tell apart
from something malicious, and the feature is not worth that.

**Every change is captured before it is made.** The prior value is journaled to disk before the
machine is touched, so a crash mid-apply still leaves `nos revert --all` able to undo it.

**Nothing is undone automatically.** There is no timer that reverts changes on its own. What the
program applies stays applied until a person reverts it.

## Updates

The updater fetches and installs code, and some of that code runs as LocalSystem, so it is worth
being precise about what protects it.

Each release publishes `SHA256SUMS.txt` and `SHA256SUMS.txt.sig`, an ECDSA P-256 signature over
that file's exact bytes. The public half of the key is compiled into the application. To install
an update, **both** must hold: the signature verifies against that key, and the downloaded
asset's SHA-256 matches its line in the signed file.

It fails closed. No key compiled in, no signature published, a signature that does not verify, or
an asset missing from the list all refuse the update and change nothing. Download URLs from the
API response are also checked to be `https` on a GitHub host before anything is fetched.

**What this does not protect against:** the signing key currently lives in a repository secret,
so somebody who takes over the GitHub account can sign a malicious release. Signing offline, on a
machine that is not the build machine, is what closes that gap —
`tools/Nostos.ReleaseTool sign` is usable that way and `docs/releasing.md` describes it. Until
then, treat the account's own security as part of this program's security.

Every release is also attested with
[build provenance](https://docs.github.com/actions/security-guides/using-artifact-attestations),
so anyone can tie a downloaded file to the commit and workflow run that produced it:

```
gh attestation verify Nostos.exe --repo rA9-001/Nostos
```

## Code signing

Releases are **not** Authenticode-signed yet, so Windows will show a SmartScreen warning and name
the publisher as unknown. That is a real gap and not one to paper over; `docs/distribution.md`
explains what signing would buy, why the plan is SignPath Foundation rather than a purchased
certificate, and what is being done in the meantime.

Never work around it by telling anyone to add an antivirus exclusion or turn off Defender.
