# services.internet-sharing

**Group:** Windows · **Improves:** Background Services · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows services that run for nothing you use. Unlike the features above, these have no name you would recognise, so each one says what it is actually for. Frees a little memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`SharedAccess`** service - Internet Connection Sharing (ICS) - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`SharedAccess` turns this PC into a NAT router for other devices. It is what backs Mobile
Hotspot, and what the old "share this connection" checkbox in adapter properties enables.

## How much this is worth, honestly

When it is not sharing anything it is idle, so this is not a framerate entry. The reason it
is worth pinning is narrower: ICS installs a DHCP and DNS proxy on the machine and hands out
addresses, and a machine that starts doing that unexpectedly is a confusing thing to debug.

Some VPN clients and virtualisation stacks - Hyper-V's Default Switch among them - depend on
this service. If a virtual network stops handing out addresses after you apply this, that is
why.

## Trade-off

Mobile Hotspot stops working. Any adapter currently sharing its connection stops sharing it.

Hyper-V's Default Switch, and the NAT networking some VPN and container tools set up, can lose
DHCP. This is the most likely of the network entries to break something you were using.

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
