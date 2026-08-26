# update.no-auto-restart

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Measured · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.
## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU`
`NoAutoRebootWithLoggedOnUsers` (REG_DWORD) -> `1`

This is a Group Policy value - "No auto-restart with logged on users for scheduled automatic
updates installations". Writing it to the registry is exactly what the policy editor does; the
policy editor is simply not present on Home editions.

## Mechanism

Windows Update installs updates and then wants to restart. Active hours push that restart out,
but they are a window rather than a veto: a restart can still be scheduled, and it can still
fire during a long evening that Windows has decided falls outside them.

With this value set, **Windows Update will not restart the machine while a user is signed in.**
It waits until nobody is. The update is still downloaded and still staged; only the restart is
withheld.

## Why "Measured"

This is not a folk remedy. It is a documented Group Policy with a documented effect, it has
existed since Windows 2000, and the behaviour is a yes/no rather than a performance claim. The
**Interruptions** promise - *stops the machine restarting in the middle of a match* - is met
exactly and literally.

## Trade-off

**Updates get installed later**, because the restart that completes them waits until you sign
out. On a machine that is never signed out that can be a long time, and a PC sitting on staged
but incomplete security updates is a real cost, not a theoretical one.

The honest recommendation is to pair this with restarting deliberately, on your own schedule,
once a week. The tweak moves the decision to you; it does not remove it.

## Revert

`nos revert update.no-auto-restart` restores the previous value, and removes it entirely if the
policy was never set - which is the usual case, so the usual revert leaves no trace in the
policy key at all.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
