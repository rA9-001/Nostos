# services.touch-keyboard

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

The start type of the **`TabletInputService`** service - Touch Keyboard and Handwriting Panel -
from `Manual` or `Automatic` to `Manual`, and stops the running instance.

## Mechanism

This service draws the on-screen touch keyboard and the handwriting input panel, and manages the
input methods behind them. On a desktop with no touchscreen and no pen it has nothing to draw.

It is filed under **Interruptions** because of what it does when it is wrong: the touch keyboard
is a top-most window, and a spurious touch event - from a graphics tablet, a touchscreen monitor
being nudged, or a driver reporting a phantom contact - can slide a keyboard across the bottom of
a fullscreen game.

## Why "Plausible"

The overlay behaviour is real and reported often enough on machines with digitisers attached.
The steady-state cost of the service on a machine that never triggers it is small and has not
been measured here.

## Trade-off

**On a laptop, tablet, or any PC with a touchscreen, do not do this** - the on-screen keyboard is
how you type. On a desktop with a physical keyboard and no touch hardware, you lose nothing you
were using.

The emoji panel (`Win`+`.`) uses this service on some builds. If it stops working after applying
this, that is why, and `manual` lets it start again on demand.

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
