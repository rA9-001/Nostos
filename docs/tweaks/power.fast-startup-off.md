# power.fast-startup-off

**Group:** Gaming · **Improves:** Crashes & Freezes · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Fixes a specific fault that shows up while playing: driver timeouts, black screens, flicker. Repairs a broken machine rather than making a working one faster.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power`
`HiberbootEnabled` (REG_DWORD) -> `0`

Default is `1` on every consumer install where hibernation is available.

## Mechanism

Shut Down does not shut Windows down. With Fast Startup on, Windows signs the user out, then
hibernates the kernel session - kernel, drivers and device state - to `hiberfil.sys`, and powers
off. The next power-on restores that image instead of initialising anything.

The consequence is that drivers can go for weeks without ever running their initialisation path.
A device left in a bad state stays in it across what the user experienced as several "restarts".
A driver update is loaded but never properly started. Firmware that expects a cold boot does not
get one.

Restart is unaffected: **Restart has always performed a full shutdown**, which is why "have you
tried restarting" fixes things that shutting down and powering on does not. That is the single
most reliable symptom of this feature.

`HiberbootEnabled = 0` makes Shut Down mean shut down.

## How much this is worth, honestly

`Plausible`, and the mechanism is not in dispute - it is documented behaviour, and the
Restart-fixes-it-but-Shutdown-does-not symptom is reproducible.

What is a claim rather than a measurement is that any given fault is caused by it. Fast Startup
is a plausible explanation for "my GPU driver went odd until I restarted", for a USB device that
stops enumerating, and for a dual-boot machine that finds its NTFS volume locked. It is not an
explanation for low framerates, and it should not be sold as one.

Filed under **Crashes & Freezes** because that is the class of fault it addresses: a machine
that has stopped working properly, not a working one running slowly.

## Trade-off

Cold boot gets slower - typically a few seconds on an NVMe machine, more on anything older. That
is the entire cost, and it is paid once per shutdown.

Hibernation and the `hiberfil.sys` file are untouched: this disables only the hybrid-shutdown
use of them.

## Revert

`nos revert power.fast-startup-off`.
