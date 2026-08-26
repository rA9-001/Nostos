# input.usb-selective-suspend-off

**Group:** Gaming · **Improves:** Input Lag & Aim · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Shortens or steadies the path from your mouse and keyboard to the screen, so the same movement always produces the same result.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Services\USB`
`DisableSelectiveSuspend` (REG_DWORD) -> `1`

The value does not exist by default. Revert deletes it rather than writing a `0`, so the stack
goes back to its own default instead of being pinned to it.

This is the global switch. It overrides the per-hub "Allow the computer to turn off this device
to save power" checkboxes in Device Manager, which is what makes it useful: those have to be
unticked one device at a time, and a device you plug in later arrives ticked.

## Mechanism

USB selective suspend lets the hub driver put an individual port into a low-power state when
the device on it has been idle. Waking it costs time -- on the order of milliseconds, sometimes
more on cheap hubs -- and the wake happens *after* the device has something to report.

For a keyboard that is invisible. For a mouse it is theoretically invisible too, because a mouse
you are holding is never idle long enough. Where it bites is everything else:

- **Wireless receivers and controllers** that idle between inputs, then need a wake before the
  first report after a pause.
- **USB audio interfaces and headsets**, which can produce an audible click or dropout.
- **Devices on a hub**, where the hub itself can be suspended and takes the whole tree with it.

The symptom is not "high latency" in a way you would measure. It is a controller that feels
dead for a moment, or a mouse that skips after you take your hand off it -- felt, not seen.

## Why "Plausible" and not "Measured"

The mechanism is documented Microsoft behaviour and the wake cost is real. What is unmeasured
is whether it happens to *your* devices, and that is almost entirely a function of your hardware:
a well-behaved wired mouse on a root port will likely never be suspended in the first place.
This is a tweak that does nothing at all for many people and fixes a maddening intermittent
problem for a few.

## Trade-off

- **Idle power draw goes up.** On a desktop this is negligible. On a laptop it is a real battery
  cost, and it applies whether or not you are gaming.
- It is machine-wide and applies to every USB device, including ones you would happily have
  suspended.
- Needs a reboot, because the hub driver reads this at load.

## Revert

`nos revert input.usb-selective-suspend-off`, then reboot.
