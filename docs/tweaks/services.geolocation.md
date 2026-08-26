# services.geolocation

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`lfsvc`** service - Geolocation Service - from `Manual (Trigger Start)`
to `Manual`, and stops the running instance.

## Mechanism

`lfsvc` works out where the machine is and hands that to apps that ask. A desktop has no GPS, so
the answer is derived from nearby Wi-Fi networks and from your IP address - accurate to a
neighbourhood, not a room.

It is filed under **Interruptions** for two reasons. The first is the consent prompt: an app
asking for location produces a dialog, and a dialog during a match is the thing this category
exists to stop. The second is that Windows shows a location-in-use indicator in the tray and
occasionally a toast about it.

## Why "Plausible"

The prompts and the tray indicator are real and directly observable. There is no measured
performance effect and this page does not claim one - the service is trigger-started and does
nothing until something asks.

## Trade-off

Anything that legitimately wants your location stops getting it: the Weather app defaults to the
wrong city, "Find my device" stops working, and the automatic time zone setting stops adjusting
when you travel.

On `manual` the service can still be started on demand, so most of that keeps working. On
`disabled` it does not, and "set time zone automatically" in Settings goes grey with no
explanation.

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
