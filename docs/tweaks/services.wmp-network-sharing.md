# services.wmp-network-sharing

**Group:** Windows · **Improves:** Unused Features · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Turns off features this PC has but you do not use -- Bluetooth, printing, Xbox and Game Pass, Fax. You already know which of these you need; each one names what stops working.

## What it changes

The start type of the **`WMPNetworkSvc`** service - Windows Media Player Network Sharing Service - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`WMPNetworkSvc` advertises this machine's media library over UPnP/DLNA and streams it to
televisions, consoles and other players on the network.

## How much this is worth, honestly

Two costs, both small and both real: it broadcasts discovery traffic on the LAN, and it holds
an index of your library.

Windows Media Player itself is not installed by default on Windows 11, but the service is often
still registered from an upgrade, at which point it is advertising a library nothing curates.

## Trade-off

Other devices on the network stop seeing this PC as a media server. If you stream from this
machine to a TV or a console, that stops.

Playing media *on* this machine is unaffected.

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
