# shell.startup-delay-off

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize`
`StartupDelayInMSec` (REG_DWORD) -> `0`

The key does not exist on a clean install; Explorer uses its built-in default.

## Mechanism

Explorer deliberately holds back everything in the Startup folder and the `Run` keys for a
window after the desktop appears - the built-in default behaves as roughly ten seconds - so that
the shell finishes drawing and becomes usable before a dozen tray applications start competing
for the disk.

Setting the value to `0` removes the hold-back. Startup items launch as soon as the shell is
ready to launch them.

## How much this is worth, honestly

`Plausible`, and it is worth being precise about what "worth" means here, because this tweak is
frequently sold as a boot-time improvement and it is not one. The machine does not finish
booting sooner. The same work happens; it happens earlier and more of it happens at once.

What you actually get is that Steam, Discord, your mouse software and your RGB daemon are
already up when you sit down, instead of appearing over the next ten seconds. What you pay is a
desktop that is unresponsive for longer, because the shell is now fighting those programs for
the disk instead of being given a head start.

On an NVMe drive that fight is short. On a SATA SSD it is noticeable. On a hard disk, do not.

Filed under **Background & Cleanup** rather than Performance because nothing about it reaches a
running game.

## Trade-off

The desktop is slower to become usable immediately after sign-in. If you habitually log in and
start clicking, you will feel this as a regression, not an improvement.

## Revert

`nos revert shell.startup-delay-off`. The captured state includes "the value was not there",
so revert removes it rather than writing a guessed default back.
