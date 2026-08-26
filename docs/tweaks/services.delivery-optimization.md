# services.delivery-optimization

**Group:** Gaming · **Improves:** Ping · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

The start type of the **`DoSvc`** service - Delivery Optimization - from `Automatic (Delayed
Start)` to `Manual`, and stops the running instance.

## Mechanism

`DoSvc` is the process that actually does peer-to-peer update distribution: downloading update
chunks from other machines, and **uploading them back out**.

Uploading saturates the upstream side of your connection, and a saturated uplink is what a ping
spike is made of - your ACKs and your input packets queue behind bulk data in a router buffer.
This is bufferbloat. It is well understood, and it does not care that the bulk data happens to
be a Windows update.

That is a direct, mechanical effect on **Ping**, which is unusual for a service tweak and is why
this one carries more weight than most of its neighbours.

## Why "Plausible" and not "Measured"

The mechanism is solid. What varies enormously is how much any given machine actually uploads:
it depends on how many peers are nearby and what Microsoft is distributing that week. Zero for
a month, then very noticeable on patch Tuesday.

Settings, Windows Update, Advanced options, Delivery Optimization, Activity monitor shows the
real number for your machine.

## Trade-off

Windows Update still works - it falls back to downloading from Microsoft over HTTP. Downloads
can be slower on a slow connection, since they can no longer be pulled from a neighbour.

Related: [`update.delivery-optimization-off`](update.delivery-optimization-off.md) sets the
policy instead of touching the service, which is the gentler of the two and the one to prefer if
you only want to stop the uploading. Doing both is redundant but harmless.

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
