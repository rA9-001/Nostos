# services.remote-registry

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`RemoteRegistry`** service from whatever it currently is to `Manual`,
and stops the running instance.

On a default Windows 11 install this service is **already `Disabled`**. Because Disabled is a
stricter form of "does not start at boot", the tweak reads as already applied on most machines
and applying it changes nothing.

## Mechanism

Remote Registry lets another machine on the network read and write this machine's registry
remotely. It is a domain administration feature.

## How much this is worth, and why it is here anyway

The performance argument is nonsense: a disabled service uses nothing, and this one is disabled
out of the box. There is no framerate to be found here and the page will not pretend otherwise.

It is filed under **Background & Cleanup** because that is where the rest of the "turn off
background services" family lives, and keeping it with its neighbours is more useful than
inventing a category for it.

The real reason it is in the catalog is different: **pinning**. Once this tool has applied it,
drift reconciliation notices if something changes it back, and the journal records what it was
before. Some remote-support tools and some enterprise management software re-enable Remote
Registry and do not mention it. Having the value watched is worth more than having it set.

## Trade-off

Remote registry administration stops working, which matters on a domain-joined machine and
nowhere else. On `manual` it starts on demand if something legitimately connects.

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
