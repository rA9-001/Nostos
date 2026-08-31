# update.active-hours

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Measured · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

`HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`
`SmartActiveHoursState` (REG_DWORD) -> `0`
`ActiveHoursStart` (REG_DWORD) -> the hour you chose
`ActiveHoursEnd` (REG_DWORD) -> the hour you chose

Hours are stored as plain numbers from 0 to 23. This is the non-policy key - the one the
Settings app itself writes - so it works on every edition of Windows.

## Mechanism

Active hours are the window in which Windows Update will not restart the machine and will not
push a restart notification. Outside that window a restart is allowed to happen on its own.

The default on a modern install is `SmartActiveHoursState = 1`, which means **Windows guesses**
the window from when the machine sees activity. That guess is fine on a laptop that is opened
at nine and shut at six, and poor on a desktop that is signed in continuously, where the signal
it is reading is "this machine is always on" and the window it picks does not have to match the
hours anybody is at the keyboard.

Setting `SmartActiveHoursState` to `0` is what makes the two hours below it stick; without it
Windows will re-derive them and overwrite whatever was set.

Windows caps the window at **18 hours**, which is why none of the options here covers the whole
day. That is a hard limit in the update client, not a choice this tweak is making.

## Why "Measured"

This is a documented setting with a documented effect and no performance claim attached: within
the window Windows Update does not restart, outside it it may. The behaviour is a yes or no and
it can be read straight back out of Settings, Windows Update, where the same numbers appear.

## Trade-off

**The machine has to restart sometime**, and this tweak decides when rather than whether. Every
hour added to the quiet window is an hour a staged security update spends installed-but-not-
active. The 18-hour option leaves six hours at night, which on a desktop that is left on is
enough and on one that is switched off at midnight is not - there, a restart simply waits for
the next opportunity.

## How it sits with the other update tweaks

- [update.no-auto-restart](update.no-auto-restart.md) is the stronger version of the same idea:
  it forbids a restart while anyone is signed in, at any hour. If you apply that one, this one
  is about the notifications more than the restart.
- [update.no-restart-notifications](update.no-restart-notifications.md) silences the toast that
  active hours only postpones.

They are complementary rather than alternatives, and nothing here conflicts.

## Revert

`nos revert update.active-hours` restores the previous three values, including
`SmartActiveHoursState`, so Windows goes back to guessing. If they were never set - the usual
case for `SmartActiveHoursState` only, since Windows writes the two hours itself - revert
removes them.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
