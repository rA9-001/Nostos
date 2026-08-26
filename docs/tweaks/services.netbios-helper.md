# services.netbios-helper

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`lmhosts`** service - TCP/IP NetBIOS Helper - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`lmhosts` provides NetBIOS-over-TCP/IP name resolution: the `\\NAME` style of addressing that
predates DNS, plus the LMHOSTS file lookup it is named after.

## How much this is worth, honestly

NetBIOS name resolution broadcasts. That is its mechanism: to find a name it asks the whole
subnet. On a quiet home LAN that is a handful of packets and costs nothing measurable.

It is on the list because it is a protocol from the early nineties still enabled by default, not
because turning it off will do anything you can see.

## Trade-off

Browsing the network by NetBIOS name stops. Old file shares addressed as `\\SERVER\share`
may stop resolving if the name is not also in DNS, which on a home network it often is not.

If you have a NAS you reach by name, test it before deciding you are finished.

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
