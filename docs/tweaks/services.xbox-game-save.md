# services.xbox-game-save

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`XblGameSave`** service - Xbox Live Game Save - from `Manual (Trigger
Start)` to `Manual`, and stops the running instance.

## Mechanism

`XblGameSave` syncs save data for Xbox and Microsoft Store titles to the cloud, so a save made
on one machine appears on another.

## How much this is worth, honestly

No measured benefit; the service is trigger-started and does nothing until a Store title writes
a save. Filed under **Background & Cleanup** alongside the other Xbox-stack entries.

## Trade-off, and why this one deserves a moment's thought

The failure mode here is worse than its neighbours' and worth stating plainly: **cloud saves
stop syncing, sometimes without an error.** You keep playing, your local saves keep working, and
you find out when you sit down at a second machine and the progress is not there.

That is exactly the kind of damage that gets attributed to the game rather than to a change you
made months earlier, which is why it is called out here rather than left implied.

If your library includes anything from Game Pass or the Store, leave this one alone. If it does
not, the service has nothing to sync.

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
