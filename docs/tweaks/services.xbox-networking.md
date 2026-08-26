# services.xbox-networking

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`XboxNetApiSvc`** service - Xbox Live Networking Service - from `Manual`
to `Manual` (usually already the case), and stops the running instance.

## Mechanism

`XboxNetApiSvc` handles the networking side of the Xbox platform: NAT type detection, Teredo
tunnelling for peer-to-peer matchmaking, and the connectivity checks the Xbox app reports on.

Nothing outside the Microsoft Store and Game Pass ecosystem uses it. A Steam or Epic title has
its own networking and never asks.

## Why it is filed under Ping, and how much it is worth

**Ping** because the service's whole job is network connectivity, so if it belongs anywhere it
belongs there. It is unproven because the claim attached to turning it off - that Teredo tunnelling
adds latency to unrelated traffic - is not supported by anything this repo can point at. Teredo
is only active for connections that use it, and a Steam match does not.

The honest summary: on a machine with no Store titles, this is a service doing nothing, and
turning it off gets you one fewer service doing nothing.

## Trade-off

**Multiplayer in Microsoft Store and Game Pass titles stops working**, or reports a strict NAT
that cannot be fixed. The Xbox app's network settings page stops being able to test anything.

On `manual` it starts on demand and multiplayer keeps working. On `disabled` matchmaking fails
with an error about your network, which will send you to your router settings for an afternoon
looking for a problem that is not there.

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
