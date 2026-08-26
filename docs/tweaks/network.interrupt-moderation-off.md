# network.interrupt-moderation-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

Turns off `*InterruptModeration` on every adapter that has it.

## Where it lives

`HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\<NNNN>`

One subkey per adapter instance. These are the same settings as the **Advanced** tab of the
adapter's properties in Device Manager, and they are stored as `REG_SZ` -- strings, even the ones
that are plainly numbers. Written as a DWORD they are silently ignored by some drivers and
rejected by others, which is a good way to believe a setting is on when it is not.

Which adapters have the keyword depends entirely on the driver. This tweak writes to the ones
that have it and **never creates it on the ones that do not**: a keyword the driver has never
heard of is not a setting that is off, it is a setting that does not exist, and inventing one
leaves configuration behind that outlives this program.

If no adapter on the machine exposes it, the tweak reports itself as not applicable rather than
pretending to have done something.

## Takes effect at the next boot

The driver reads its configuration when the adapter initialises. Disabling and re-enabling the
adapter in Device Manager does it too, and drops your connection while it happens -- so this
asks for a reboot instead of doing that to you mid-session.

## Mechanism

When a packet arrives, the NIC has to interrupt the CPU so the driver can pick it up. At a
few hundred thousand packets per second that is a lot of interrupts, so the card is allowed to
wait a short while and collect several packets into one interrupt instead.

The wait is the feature. Typical moderation timers are tens to a couple of hundred
microseconds, and every received packet pays it -- including the one carrying the position of
the player about to shoot you.

Turning it off means one interrupt per packet: the packet reaches the stack as soon as it
arrives, and the CPU does more work per byte.

## How much this is worth, honestly

`Plausible`, and this is one of the stronger entries under Ping. The mechanism is not in
dispute, it is documented by every NIC vendor, and Microsoft's own low-latency networking
guidance names interrupt moderation as something to disable when latency matters more than
throughput.

What is not established is the size on a home connection. The delay it removes is measured in
microseconds; the internet leg of your ping is measured in milliseconds. If you play on a
server 30 ms away, this is a fraction of a percent of the round trip, and you will not see it
in a ping graph.

Where it is worth more than that: LAN play, a local server, and frametime consistency on a
machine whose CPU was being interrupted in bursts rather than steadily.

## Trade-off

Higher CPU use under heavy network load, because the CPU is now interrupted per packet. On a
modern desktop with a gigabit link this is not measurable; on a low-power CPU saturating a
10 GbE link it very much is.

If the machine also does bulk transfers -- a NAS, a seedbox, large Steam downloads -- this
trades a little of that throughput away.

## Revert

`nos revert network.interrupt-moderation-off` restores the exact prior string on every adapter it
changed, including deleting nothing -- absent keywords were never written, so there is
nothing to clean up. Takes effect at the next boot, like the apply.
