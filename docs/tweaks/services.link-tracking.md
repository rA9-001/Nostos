# services.link-tracking

**Group:** Windows · **Improves:** Background Services · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows services that run for nothing you use. Unlike the features above, these have no name you would recognise, so each one says what it is actually for. Frees a little memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`TrkWks`** service - Distributed Link Tracking Client - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`TrkWks` maintains links between NTFS files and the shortcuts that point at them, using the
volume's object IDs, so that a shortcut keeps working after its target is renamed or moved.

## How much this is worth, honestly

The bookkeeping is per-file and genuinely cheap, and it is the kind of thing that shows up in
tweak lists precisely because it sounds expensive and is not.

The reason it survives on this list is different: object ID tracking writes to the NTFS metadata
of files you only read, which is the same class of write that `storage.ntfs-last-access-off`
turns off. If you are removing metadata writes, remove both or neither.

## Trade-off

Shortcuts stop repairing themselves when their target moves. You get "the item this shortcut
refers to has been changed or moved" instead of the file.

Nothing else notices.

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
