# services.retail-demo

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`RetailDemo`** service - Retail Demo Service - from `Manual` to
`Manual`, or from `Automatic` if something has set it that way, and stops the running instance.

## Mechanism

Retail Demo Mode is the mode a laptop runs in while it is sitting on a shop shelf: a looping
demonstration, a reset-on-idle behaviour, and a set of preinstalled demo content. `RetailDemo`
is the service that drives it.

There is no case for it on a PC somebody owns.

## How much this is worth, honestly

The rating is a little higher than its neighbours for one reason: on machines bought from a
retailer that actually used demo mode, this service and its content are sometimes **left
active**, and it does then do periodic work. On a clean install it is inert.

So: `Plausible` on the machines where it matters, and irrelevant on the rest. Filed under
**Background & Cleanup** because the periodic work is the only claim being made.

## Trade-off

None that applies to a machine in normal use. Retail Demo Mode stops being available, which is
only a loss if you are a shop.

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
