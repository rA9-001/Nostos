# power.modern-standby-off

**Group:** Gaming · **Improves:** Crashes & Freezes · **Risk:** Experimental · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Fixes a specific fault that shows up while playing: driver timeouts, black screens, flicker. Repairs a broken machine rather than making a working one faster.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\Power`
`PlatformAoAcOverride` (REG_DWORD) -> `0`

The value does not exist on a clean install.

## Mechanism

Modern Standby - S0 Low Power Idle, historically "Always On, Always Connected" - is a sleep
state in which the machine never actually leaves S0. The CPU idles at its deepest package state,
the screen is off, and the network stays up so that mail, Windows Update and Store apps can keep
running on a schedule.

On a phone that is the correct design. On a desktop it produces the complaints this tweak
exists for: fans audibly spinning in a "sleeping" machine, a PC that wakes itself at 3am, power
draw that never drops, and a machine that gets warm inside a closed cabinet.

`PlatformAoAcOverride = 0` tells Windows to ignore the platform's Modern Standby capability and
use classic S3 suspend-to-RAM instead - power actually cut to the CPU, memory held in
self-refresh.

Check what you have first: `powercfg /a` lists the supported states.

## How much this is worth, honestly

`Plausible`, and it is rated **Experimental** for risk rather than for evidence. Where it works,
it works completely and obviously: the machine sleeps, stays asleep, and draws almost nothing.

The reason for the risk rating is that S3 has to be supported by the firmware, and on a lot of
recent boards it simply is not. Modern Standby-only systems have been shipping since about 2019,
and on those this value produces a machine that either refuses to sleep at all or sleeps and
does not come back. Some vendors expose an S3 option in UEFI and some do not.

Filed under **Crashes & Freezes** because a machine that will not sleep, or wakes itself, is a
fault - not a machine that needs more frames.

## Trade-off

If the firmware does not support S3, the likely outcomes are a Sleep option that disappears from
the Start menu, a machine that hangs on resume, or wake-from-keyboard that stops working. Revert
and reboot fixes it, but you may have to do that from a hard power cycle.

Features that depend on staying connected while asleep - waking for a Wake-on-LAN packet handled
in software, background Store app updates overnight, "Find my device" - stop.

Needs a reboot to take effect, and another to undo.

## Revert

`nos revert power.modern-standby-off`, then reboot. Revert removes the value rather than writing
a `1`, because "absent" and "1" are not the same thing to the power manager.
