# privacy.activity-history-off

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\System`

| Value | Set to |
| --- | --- |
| `EnableActivityFeed` (REG_DWORD) | `0` |
| `PublishUserActivities` (REG_DWORD) | `0` |
| `UploadUserActivities` (REG_DWORD) | `0` |

None of the three exist on a clean install.

## Mechanism

Activity History is the data behind Timeline: which app you had open, which document, and when,
recorded locally and - with the third value on - synchronised to your Microsoft account so it can
be resumed on another device.

The three values are a chain. `EnableActivityFeed` turns the feature off entirely.
`PublishUserActivities` stops applications writing entries. `UploadUserActivities` stops what is
already collected leaving the machine. Setting all three closes the collection and the upload
rather than only hiding the UI.

Timeline itself - the Task View history - was removed from Windows in 2021. The collection
policy was not removed with it.

## How much this is worth, honestly

`Plausible`, and the claim is a privacy one, not a performance one. The recording is cheap; it is
a database write per app launch. Anyone telling you this frees resources is guessing.

What it does do is stop a list of what you use and when being kept and, in the default
configuration, synchronised to Microsoft. Whether that is worth doing is a judgement about
data, and the catalog's job here is to make it a journaled, revertible one instead of a
half-remembered registry edit.

Filed under **Background & Cleanup**, which is the honest home: it stops Windows doing something
you did not ask for, and makes no promise about frames.

## Trade-off

"Pick up where you left off" across devices stops working. On current Windows builds there is
very little left of the feature to lose.

Machine-scoped policy: it applies to every account on the PC.

## Revert

`nos revert privacy.activity-history-off` removes all three values, since absent is what was
captured.
