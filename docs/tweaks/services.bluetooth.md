# services.bluetooth

**Group:** Windows · **Improves:** Unused Features · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Turns off features this PC has but you do not use -- Bluetooth, printing, Xbox and Game Pass, Fax. You already know which of these you need; each one names what stops working.

## What it changes

The start type of the **`bthserv`** service - Bluetooth Support Service - from `Manual` or
`Automatic` to `Manual`, and stops the running instance.

## Mechanism

`bthserv` discovers Bluetooth devices, manages pairing, and keeps paired devices working. On a
machine with no Bluetooth radio it has nothing to do; on one with a radio, it is what makes your
Bluetooth mouse, headset or controller function at all.

## Read this before applying it

**If you use any Bluetooth peripheral, do not do this.** The failure mode is a device that
silently stops pairing or stops reconnecting after a reboot, with nothing anywhere saying why.
That is exactly the kind of damage this project exists to avoid causing, and the only reason the
tweak is offered is that plenty of desktops genuinely have no Bluetooth hardware and no
Bluetooth peripherals.

Check first: Settings, Bluetooth and devices. If the page has no Bluetooth toggle at all, your
machine has no radio and this is free.

## How much this is worth, honestly

There is no measured framerate benefit and no reason to expect one - an idle service with no
paired devices does nothing. It is filed under **Background & Cleanup** with the other
background-service entries. What it actually buys you is one fewer resident service on a machine
that cannot use it.

## Trade-off

Bluetooth stops working. On `manual` Windows can still start the service when something asks for
Bluetooth, which is the safety net that makes a wrong guess here recoverable. On `disabled` it
cannot, and pairing fails with no useful message.

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
