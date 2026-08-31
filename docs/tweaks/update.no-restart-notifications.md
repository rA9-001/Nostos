# update.no-restart-notifications

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU`
`SetAutoRestartNotificationDisable` (REG_DWORD) -> `1`

`HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`
`RestartNotificationsAllowed2` (REG_DWORD) -> `0`

The first is the Group Policy *Turn off auto-restart notifications for update installations*.
The second is the switch behind *Notify me when a restart is required to finish updating* in
Settings. Both, because they are read by different parts of the update experience and either one
alone leaves a path open.

## Mechanism

When an update is staged and waiting for a restart, Windows raises a notification saying so, and
repeats it. On a machine that is left on for days that is not one notification, it is a series
of them.

A toast is not a passive thing. It is a window, it animates in, and depending on what it is and
what has focus it can pull focus away from the foreground application. In a borderless-fullscreen
game that is a dropped frame or a stutter; in an exclusive-fullscreen game it can be a minimise,
which costs a mode change on the way out and another on the way back.

This does not stop the update, the staging, or the eventual restart. It stops being told about
it while it waits.

## Why "Plausible"

The mechanism is documented and the notification demonstrably stops appearing. What is not
measured here is the *frames* half of the claim: how much a given toast costs depends on the
game's presentation mode, the compositor, and whether it takes focus at all, and this project
does not have a rig set up to put a number on that. The interruption itself is real and
observable; the frame cost is reasoning from how Windows composites, so the rating says
Plausible.

## Trade-off

**You stop being told the machine is waiting to restart.** That is the entire point and it is
also the entire cost: a PC can now sit for a fortnight with a security update staged and never
mention it, and the only thing that would tell you is opening Settings.

Pair it with restarting deliberately, on your own schedule, once a week. This tweak removes the
reminder, not the reason for it.

## How it sits with the other update tweaks

[update.active-hours](update.active-hours.md) postpones the restart itself and quietens the
notification only inside its window. [update.no-auto-restart](update.no-auto-restart.md)
forbids the restart while you are signed in. This one is specifically about the toast, which
neither of those removes on its own.

## Revert

`nos revert update.no-restart-notifications` restores both values, and removes them entirely if
they were never set - which is the usual case for the policy value. Notifications resume the
next time an update is staged.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
