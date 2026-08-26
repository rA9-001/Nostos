# services.printer-notifications

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

The start type of the **`PrintNotify`** service - Printer Extensions and Notifications - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`PrintNotify` hosts the printer manufacturer's own UI extensions and raises the notifications
that come with them: out of paper, low toner, job complete, driver wants attention.

It is a different service from `Spooler`. The spooler queues the job; this is the half that puts
a window on your screen about it.

## How much this is worth, honestly

A printer notification arriving mid-match is a focus steal, and it is one of the few
interruptions on this list that is genuinely common on a machine with an inkjet attached -
those drivers are enthusiastic about telling you the cartridge is low.

With `Spooler` also stopped there is nothing to notify about, so these two are usually applied
together. Applying only this one keeps printing working and silences the chatter.

## Trade-off

Printer status popups stop. Vendor control panels that piggyback on this service may report
that the printer is offline, or lose features such as ink-level display.

Printing itself is unaffected: that is the spooler's job.

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
