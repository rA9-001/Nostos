# services.program-compatibility

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

The start type of the **`PcaSvc`** service - Program Compatibility Assistant - from `Automatic`
to `Manual`, and stops the running instance.

## Mechanism

PCA watches processes as they start and exit, looking for patterns associated with known
compatibility problems: an installer that exits with a failure code, an old application that
crashes on start-up, a program that tries to write to a protected location.

When it finds one it offers to apply a compatibility shim, in a dialog. **That dialog takes
focus.** A dialog taking focus while a fullscreen game is running is either an alt-tab you did
not ask for or a game that minimises, which is why this is filed under **Interruptions** and not
under Interruptions.

PCA also maintains a per-process watch list, which is a small amount of work on every process
launch.

## Why "Plausible"

The focus-stealing dialog is real and reproducible with any application that trips PCA's
heuristics. The steady-state cost of the process watching is small and unmeasured here.

## Trade-off

Windows stops offering to fix old applications automatically. If you play games from the 2000s,
PCA is occasionally the thing that quietly makes one work, and you would be turning that off.
Compatibility settings you apply by hand, through a shortcut's Properties, are unaffected -
those are a different mechanism.

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
