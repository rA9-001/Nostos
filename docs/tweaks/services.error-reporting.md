# services.error-reporting

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

The start type of the **`WerSvc`** service - Windows Error Reporting - from `Automatic` (it is
usually `Manual` already on Windows 11, in which case this reads as applied and changes nothing)
to `Manual`, and stops the running instance.

## Mechanism

WER is the service behind "Windows is checking for a solution to the problem". When an
application crashes it collects a dump, compresses it, and uploads it to Microsoft.

That is disk and network work happening **at the exact moment a game has just died and you are
trying to get back into the match**. On a large game process the dump can be gigabytes and can
keep the disk busy well past the point where you have already relaunched.

It is filed under **Interruptions** rather than FPS because the cost is not a steady drag on
framerate: it is a burst of work at the worst possible moment, plus a dialog.

## Why "Plausible"

The dump-and-upload behaviour is documented and easy to observe. What has not been measured here
is how long it takes for a given game on a given machine, which varies with the process size by
orders of magnitude.

## Trade-off

Application crash dumps stop being collected and uploaded. If you ever need to send a crash
report to a game developer or to Microsoft support, you will need to turn this back on first.
Local crash logging in Event Viewer is unaffected.

## What "Manual" and "Disabled" actually mean

`--set start=<option>`, or the radio buttons in the app.

| Option | Start type | What it means |
| --- | --- | --- |
| `manual` | `SERVICE_DEMAND_START` | **Default and recommended.** The service no longer starts at boot, but anything that asks for it can still start it. |
| `disabled` | `SERVICE_DISABLED` | The service cannot start at all. Anything that needs it fails. |

**Manual is a safety net.** If the reasoning on this page turns out to be wrong for your machine,
Manual means the service starts on demand and you never find out there was a problem. Disabled
means whatever needed it fails with an error naming neither the service nor this tool, weeks
after you ran it.

Pick `disabled` only after checking that the service keeps starting itself on Manual and
deciding you would rather it did not.

## What revert does

`nos revert <id>` restores the **exact** start type captured before the change, including the
difference between `Automatic` and `Automatic (Delayed Start)` - two settings the SCM reports
identically and that a "restore defaults" would collapse into one.

The service is not restarted by revert. Its start type says what should happen at the next boot,
and starting a service that was deliberately stopped is a larger intervention than a revert was
asked to make.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
