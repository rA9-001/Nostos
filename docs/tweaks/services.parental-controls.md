# services.parental-controls

**Group:** Windows · **Improves:** Unused Features · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Turns off features this PC has but you do not use -- Bluetooth, printing, Xbox and Game Pass, Fax. You already know which of these you need; each one names what stops working.

## What it changes

The start type of the **`WpcMonSvc`** service - Parental Controls - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`WpcMonSvc` enforces Microsoft Family Safety on this machine: screen time limits, app and
content filters, and the activity reporting that feeds the weekly email.

## How much this is worth, honestly

If no family group covers this machine, the service has no policy to enforce and does
nothing.

If one does, this is not a performance tweak - it is the enforcement mechanism, and stopping it
is the thing the policy exists to prevent. The catalog offers it because that is a decision for
whoever owns the machine, and because doing it here leaves a journal entry rather than not.

## Trade-off

Family Safety limits, filters and activity reporting stop being enforced on this machine.

If you are the parent rather than the child, this is a regression and not a tweak.

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
