# update.pin-windows-version

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Measured · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`
`TargetReleaseVersion` (REG_DWORD) -> `1`
`ProductVersion` (REG_SZ) -> `Windows 11`
`TargetReleaseVersionInfo` (REG_SZ) -> the version you chose, e.g. `25H2`

This is the Windows Update for Business policy *Select the target Feature Update version*. It is
the current, documented mechanism for holding a machine on one release, and it replaces the
older `DeferFeatureUpdatesPeriodInDays` approach that other tools still write.

## Mechanism

Windows ships two kinds of update through the same client. A **quality update** is the monthly
one: a few hundred megabytes, a normal restart, security fixes. A **feature update** is 25H2
replacing 24H2: several gigabytes, a restart that can take forty minutes, and a machine that
comes back with some settings back at their defaults - including some this program sets.

`TargetReleaseVersion` tells the update client which feature update version this machine is
allowed to be on. Once the machine is on that version, feature updates stop being offered.
**Quality and security updates continue to arrive on their normal schedule**, which is the
distinction that makes this different from turning Windows Update off.

## Why "Measured"

Documented policy, documented effect, and a yes-or-no outcome that can be checked: after this,
the feature update stops being offered, and Settings, Windows Update says so. There is no
performance claim attached that would need measuring.

## Trade-off, and why this one is Moderate rather than Safe

**A pin has an expiry date, and Windows will not remind you.** Every Windows release has an
end-of-servicing date. When the pinned version reaches it, the machine stops receiving security
updates entirely - not with an error, just with nothing arriving. That is a genuinely bad state
to be in and it is why this is rated Moderate while the other update tweaks here are Safe.

Set a reminder for a few months before the version you pinned goes out of service, and either
move the pin or revert this tweak.

**Naming a version you are not on is an upgrade, not a hold.** If this machine is on 24H2 and
the pin says 25H2, Windows will install 25H2 and then stop - the exact feature update you were
trying to avoid, triggered by the tweak meant to prevent it. Check Settings, System, About
before choosing, and pick the version that is already there.

## Editions

**Pro, Enterprise and Education only.** Windows Update for Business policies are not read by the
update client on Home: the value writes successfully, sits in the registry, and does nothing.
Nostos checks the edition and reports this tweak as not applicable on Home rather than letting
it apply and verify cleanly while changing nothing.

## Revert

`nos revert update.pin-windows-version` removes all three values, and the machine becomes
eligible for the next feature update again on its normal schedule.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
