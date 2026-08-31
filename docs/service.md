# The service

`Nostos.Service.exe` is the privileged half. It exists for one reason: **applying a
tweak mid-session must not raise a UAC prompt.** Elevate once at install; after that the CLI
and the UI are ordinary unprivileged clients.

## Install

**Normally you do not.** Launching `Nostos.exe` installs and starts the service for
you on first run, with a single administrator prompt. Everything below is for the cases where
you want to do it by hand.

```
nos service install     # install and start, raises the UAC prompt for you
nos service status
```

Or directly, from an elevated prompt:

```
Nostos.Service.exe setup
```

`setup` installs *and* starts in one elevated invocation, which is why the app uses it: the
user sees one prompt rather than one per step.

Install registers the service as **LocalSystem**, **delayed auto-start**, with **restart on
failure** (5s, 15s, 60s). Delayed start keeps it out of the boot critical path; restart-on-
failure exists so that a crash does not silently leave the machine without drift reconciliation
and without a way to apply machine-scope tweaks from an unelevated app.

Install also writes `%ProgramData%\Nostos\service.json` recording the SID of
the account that ran it. That account is the one allowed on the control pipe.

`setup` is idempotent, and re-running it re-points an existing registration at the copy it was
run from. That matters more than it sounds: the SCM stores an absolute path, so moving or
deleting the folder you first ran from leaves a service that works until the next reboot and
then never starts again. Re-running setup from the new location repairs it, stopping the old
process first — a running service goes on executing the binary it was started with, so a
repoint without the stop would report success and change nothing.

The app checks this at every launch. If the registered path is gone it says so in the banner
and offers **Repair**, which is the same one-prompt setup. If the path merely differs from the
copy you launched, it says which folder is in charge and changes nothing — running a second
copy is not by itself a reason to take the service away from the first.

## Removing it

`Nostos.Service.exe uninstall` stops and deregisters the service and changes nothing else:
tweaks stay applied and `%ProgramData%\Nostos` stays where it is.

`Nostos.Service.exe remove` is the harder version -- service *and* data folder -- and is what
the app's **Remove Nostos from this PC** runs, elevated, after it has reverted everything
through the ordinary path. It takes no arguments deliberately; see
[docs/uninstall.md](uninstall.md).

## Using it

Add `--service` to any command to route it through the daemon instead of the local engine:

```
nos apply graphics.hags --service      # machine scope, from an ordinary shell, no prompt
nos status --service
nos revert --all --service
```

Without `--service`, the CLI drives the engine directly and machine-scope tweaks need an
elevated shell. Both paths write the same journal, so they interoperate.

## What it does in the background

| Job | Interval | Purpose |
| --- | --- | --- |
| Control pipe | — | 4 concurrent connections, newline-delimited JSON |
| Reconciler | 30 min | Re-applies tweaks Windows has reset |

Both are cancelled together on stop.

**The service never undoes a change on its own.** Reconciliation puts back what *this program*
applied and Windows reset; it never removes something you asked for. There is no timer waiting
for you to confirm that the machine still works.

**The service does not watch what you run.** It never enumerates processes and never reacts to
a game starting. See [architecture.md](architecture.md) for why that was removed rather than
made optional.

## Configuration

`%ProgramData%\Nostos\service.json`:

```json
{
  "allowedSids": ["S-1-5-21-..."],
  "reconcileMinutes": 30
}
```

`allowedSids` is written for you at install time with the SID of the account that ran setup.
It is the security-critical field: it lists the accounts permitted to drive a
LocalSystem process that can rewrite HKLM. **Never put a broad group there** — not Users, not
Authenticated Users, not Everyone. Doing so turns the control pipe into a local privilege
escalation. SYSTEM and Administrators are always allowed and are not listed.

If the file is missing or unreadable the service still runs, but only SYSTEM and administrators
can reach it. That is the safe direction to fail in.

## Development: console mode

```
Nostos.Service.exe --console
```

Runs the same daemon in the foreground under your own account, with debug logging echoed to
the terminal. No install, no elevation, no SCM. If `service.json` does not exist it allows the
current user on the pipe for that run only.

This is how the pipe and the reconciler get exercised during development, and it is the first
thing to try when troubleshooting.

## Logs

`%ProgramData%\Nostos\logs\service-YYYYMMDD.log`, rotated daily, kept 14 days.

The log records what the service was *doing*, including decisions that produced no change. The
journal (`journal.jsonl`) records what actually *changed*. Ask for both when triaging an issue.

## Known limitations

**One outstanding change per tweak.** The journal keys outstanding changes by tweak id.
Applying a tweak twice under different options without reverting in between leaves one
snapshot, the older one. Declarative tweaks capture every value any option could write, so a
revert still puts all of them back; a native tweak with options has to handle that itself.

Fixing this properly means adding an instance key to the journal and to `GetOutstandingAsync`.
That is a contained change to `Core`, not a redesign.

**User-scoped tweaks are refused over IPC.** A LocalSystem service writing to `HKCU` writes to
SYSTEM's own hive: the change would appear to succeed, verify would even pass, and the user's
setting would be untouched. The daemon detects that it is running as SYSTEM and refuses these
with an explanation rather than misapplying them. Run them from the CLI as the signed-in user.

Reads have the same problem and are more dangerous, because they do not fail: asking the
service about a user-scoped tweak returns SYSTEM's answer, which is usually "not set". The
desktop app and the CLI both avoid this by doing user-scoped work in their own process, where
the correct hive is already loaded — see `SplitBackend` in the app.

The real fix is impersonating the console session — `WTSQueryUserToken` on the active session,
then `ImpersonateLoggedOnUser` around the read and the write. Not implemented yet.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `nos --service` says the service is not running | Not installed, or stopped. `nos service status`. |
| "refused this account" | The caller's SID is not in `allowedSids`. Reinstall from that account, or add the SID. |
| Protocol mismatch | The CLI and the installed service are different builds. Reinstall the service. |
| Service was running, then stopped after a reboot | Its registered path no longer exists. Re-run `setup` from the current folder. |
| Publish or build fails with a file lock | The service is running from the folder being written. Stop it first. |
| Games are not detected automatically | By design; there is no process watcher. Apply a profile before you launch. |
| Access denied creating additional pipe instances | The process account lacks `CreateNewInstance` on the pipe. Only the first listener starts; the service is capped at one client. |
