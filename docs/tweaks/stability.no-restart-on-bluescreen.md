# stability.no-restart-on-bluescreen

**Group:** Gaming · **Improves:** Crashes & Freezes · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Fixes a specific fault that shows up while playing: driver timeouts, black screens, flicker. Repairs a broken machine rather than making a working one faster.
## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\CrashControl`
`AutoReboot` (REG_DWORD) -> `0`

The same switch as System Properties, Advanced, Startup and Recovery, "Automatically restart".

## Mechanism

When Windows hits a bug check it shows the stop screen, writes a dump, and - by default -
**restarts immediately**. On a fast machine the screen is gone in a couple of seconds, often
before the QR code has finished drawing.

The stop code on that screen is the single most useful piece of information you are going to
get. `VIDEO_TDR_FAILURE` and `WHEA_UNCORRECTABLE_ERROR` are different problems with different
fixes: the first points at the GPU driver, the second at hardware, usually memory or an unstable
overclock. Without the code you are guessing, and the guess is normally "reinstall the driver",
which fixes one of them.

With `AutoReboot = 0` the machine sits on the stop screen until you restart it yourself. You get
to read the code and photograph it.

## Why this is filed under Crashes & Freezes

The category promise is that a tweak here *fixes a specific fault - repairs a broken machine
rather than making a working one faster*. This one does not fix the fault; it makes the fault
legible. That is a fair reading of the same promise: on a machine that is bluescreening, the
thing standing between you and a fix is not knowing what the fault is.

It changes nothing whatever on a machine that is not crashing.

## Why "Plausible" and not "Measured"

There is nothing to measure - the behaviour is documented and binary. It is `Plausible` rather
than `Measured` because the *benefit*, that you end up diagnosing the problem correctly, depends
on what you do with the stop code, which is not something this repo can claim on your behalf.

## Trade-off

**A crash while you are away from the machine now leaves it sitting on a stop screen** instead of
coming back. On a PC that hosts a game server, runs unattended, or gets remoted into, that is a
real cost: it stays down until somebody presses the button. On a desktop you sit in front of, it
is exactly what you want.

The dump file is written either way, so WinDbg and BlueScreenView still work. This tweak is
about the version of the information you can read without them.

## Revert

`nos revert stability.no-restart-on-bluescreen` restores the previous value, deleting it if it
was never set. Takes effect immediately - no reboot needed, since the value is read at crash
time. **Machine-scoped**, needs elevation.
