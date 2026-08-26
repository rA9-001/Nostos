# input.device-queue-size

**Group:** Gaming · **Improves:** Input Lag & Aim · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Shortens or steadies the path from your mouse and keyboard to the screen, so the same movement always produces the same result.
## What it changes

| Key | Value |
| --- | --- |
| `HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters` | `MouseDataQueueSize` (REG_DWORD) |
| `HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters` | `KeyboardDataQueueSize` (REG_DWORD) |

Both to `50`, `20` or `100`. **Requires a reboot** - the class drivers allocate the buffers when
they load.

## Mechanism

`mouclass` and `kbdclass` are the class drivers sitting above the individual mouse and keyboard
port drivers. Each keeps a queue of input packets that have arrived from the device but have not
yet been read by whatever is listening. Windows allocates 100 entries for each.

## What this actually does, stated plainly

The tweak is everywhere and the reasoning given for it is backwards.

The queue is **not a delay line.** Packets are not held in it for a fixed period and then
released. They sit in it exactly as long as it takes the reader to get to them, which on an idle
system is microseconds. A smaller queue does not make the reader faster.

What a smaller queue changes is **what happens when the reader stalls.** If something blocks
input processing for longer than the queue can absorb, the queue overflows and packets are
**dropped**. With 100 entries at a 1000Hz polling rate you can absorb a 100ms stall without
losing anything. With 20, you start losing mouse movement after 20ms.

So the honest description is: this reduces how much input your machine can survive losing, in
exchange for a smaller kernel allocation. It is filed under **Input Lag & Aim** because that is
the category it is always recommended for, and this page has to be the place that says the
recommendation is wrong.

It is in the catalog because people set it regardless, and setting it here means it is
journaled, revertible, and accompanied by this paragraph.

## Options

`--set size=<option>`.

| Option | Value | What it means |
| --- | --- | --- |
| `smaller` | `50` | **Default.** Half the shipping value. Still 50ms of headroom at 1000Hz. |
| `smallest` | `20` | The forum value. 20ms of headroom, and dropped input under any stall. |
| `windows-default` | `100` | The shipping value, written explicitly. Useful for repairing a machine some other tweak pack has been through. |

## Trade-off

Under load - a shader compile, a stutter, a badly behaved overlay - input is dropped rather than
delivered late. Dropped mouse movement is worse than late mouse movement, because late movement
still gets you where you were aiming.

## Revert

`nos revert input.device-queue-size` restores both previous values, deleting either if it did
not exist. Takes effect at the next boot. **Machine-scoped**, needs elevation.
