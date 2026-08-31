# services.downloaded-maps

**Group:** Windows · **Improves:** Unused Features · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Turns off features this PC has but you do not use -- Bluetooth, printing, Xbox and Game Pass, Fax. You already know which of these you need; each one names what stops working.

## What it changes

The start type of the **`MapsBroker`** service - Downloaded Maps Manager - from `Automatic
(Delayed Start)` to `Manual`, and stops the running instance.

## Mechanism

`MapsBroker` downloads offline map data for the Maps app and keeps it up to date in the
background. Map regions are large - hundreds of megabytes to several gigabytes - and the updates
arrive on Windows's schedule, not yours.

Unlike most of the service entries in this catalog, this one does **real, sustained work**: disk
writes and a bulk download, unprompted. That is why it is rated `Plausible` rather than
it is filed under **Background & Cleanup** rather than being written off as a
no-op. A bulk download landing mid-match is both a disk-contention hitch and a bandwidth spike.

## Why "Plausible" and not "Measured"

The download behaviour is documented and observable in Task Manager when it fires. How often it
fires depends on whether you have downloaded any offline maps at all - and if you never have,
which is the common case, the service is idle and this tweak buys you nothing.

Check first: Settings, Apps, Offline maps. If the list is empty, applying this is close to a
no-op.

## Trade-off

Offline maps stop updating, and downloading new ones through Settings stops working while the
service is not running. On `manual` Windows starts it when you use that page, so in practice you
only lose the unattended background updates - which is exactly the part you wanted gone.

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
