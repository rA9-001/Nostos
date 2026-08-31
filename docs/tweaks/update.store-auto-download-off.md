# update.store-auto-download-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\WindowsStore`
`AutoDownload` (REG_DWORD) -> `2`

The Group Policy *Turn off Automatic Download and Install of updates*. `2` is off; `4` is the
explicit on. The key does not exist on a clean machine, so applying this creates it and revert
removes it again.

## Mechanism

The Microsoft Store is a **second updater**, independent of Windows Update, with its own
schedule and its own settings. Nothing in Settings, Windows Update touches it, and none of the
other update tweaks here affect it. On a machine with a few Store apps installed - and Windows
11 ships with a couple of dozen - it will start a download whenever it feels like it.

The problem is not the download itself, it is what a saturated link does to a game. A game's
network traffic is small and continuous, and it is extremely sensitive to queueing delay: the
moment something else fills the pipe, every packet the game sends waits behind it in the router
buffer. That is what a ping graph looks like when it goes from 25 ms to 300 ms for ninety
seconds and then comes back. The download does not have to be large to do it, only concurrent.

## Why it is filed under Ping

Same reasoning as [update.delivery-optimization-off](update.delivery-optimization-off.md), and
deliberately consistent with it: a background transfer competing for the same link is a latency
problem, not a bandwidth one. Filing it under Interruptions would be describing how it feels
rather than what it does.

## Why "Plausible"

The registry value and its effect are documented, and the Store demonstrably stops
auto-downloading. What is not measured is the ping claim, because the size of the effect depends
entirely on your connection and your router's queue management - on a line with decent AQM a
background download costs very little, and on a cheap ISP router it costs a great deal. The
mechanism is sound and well understood; the number is yours, not ours.

## Trade-off

**Store apps stop updating themselves.** They will keep working, but they sit at the version
they were at. If something on this machine matters and comes from the Store, you now have to
open the Store and press *Get updates* yourself from time to time.

Manual updates still work exactly as before. Only the automatic schedule is switched off.

## Editions

**Pro, Enterprise and Education only.** The Store policy is not honoured on Home, so Nostos
reports this tweak as not applicable there rather than writing a value that would sit in the
registry doing nothing.

## Revert

`nos revert update.store-auto-download-off` removes the value and the Store resumes its normal
schedule.

Verified on a machine that had never had the key: apply created
`Policies\Microsoft\WindowsStore` with `AutoDownload = 2`, revert removed the value and left
the key behind, empty. That is what revert does everywhere in this program - it restores
**values**, not key structure - and an empty policy key changes nothing. Worth stating rather
than implying a cleaner undo than the one that actually happens.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
