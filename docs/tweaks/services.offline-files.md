# services.offline-files

**Group:** Windows · **Improves:** Background Services · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows services that run for nothing you use. Unlike the features above, these have no name you would recognise, so each one says what it is actually for. Frees a little memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`CscService`** service - Offline Files - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`CscService` runs the client-side cache: a local copy of network shares that keeps them
readable when the network is not, and syncs the changes back when it returns.

## How much this is worth, honestly

This is a domain feature. It only does work when folder redirection or an explicitly
offline-enabled share is configured, which on a home machine is never.

Where it *is* configured, the sync it performs is real disk and network work that arrives on
reconnect - but a machine with redirected folders is a managed machine, and this tool is not
aimed at those.

## Trade-off

Redirected folders and offline-enabled network shares stop being available when the network
is down, and stop syncing when it comes back.

On a machine with no network shares configured, nothing changes.

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
