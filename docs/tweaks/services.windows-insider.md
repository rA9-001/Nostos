# services.windows-insider

**Group:** Windows · **Improves:** Unused Features · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Turns off features this PC has but you do not use -- Bluetooth, printing, Xbox and Game Pass, Fax. You already know which of these you need; each one names what stops working.

## What it changes

The start type of the **`wisvc`** service - Windows Insider Service - from `Manual (Trigger
Start)` to `Manual`, and stops the running instance.

## Mechanism

`wisvc` enrols the machine in the Windows Insider Program and handles the flighting checks that
come with it: which ring you are on, whether a preview build is available, and the feedback
plumbing that goes with a preview install.

**It does nothing at all unless you have joined the Insider Program.** On a machine that has
not, it is trigger-started and never triggered.

## How much this is worth, honestly

Because the honest answer to "what does this buy me" on a normal machine is "nothing", and the
page should say so rather than dress it up. Filed under **Background & Cleanup** with the rest of
the background-service family.

Like several of its neighbours, the actual value here is **pinning**: the setting is journaled,
and drift reconciliation will say if something turns it back on.

## Trade-off

You cannot join the Insider Program or receive preview builds while this is off. If you are
already on a preview build, do not do this - `manual` will let it start on demand, but you have
no reason to be turning it off in the first place.

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
