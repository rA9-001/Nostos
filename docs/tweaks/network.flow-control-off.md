# network.flow-control-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

Turns off `*FlowControl` on every adapter that has it.

The values are a small bitfield: `0` disabled, `1` transmit pause frames, `2` receive them,
`3` both -- which is the usual default.

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

802.3x flow control lets a device whose receive buffer is filling up send a PAUSE frame to
the other end of the link, telling it to stop transmitting for a specified time.

The problem is granularity. A PAUSE frame stops **the entire link**, not the connection that
caused the congestion. A large download filling a buffer can therefore pause the link that your
game's packets were about to arrive on, for as long as the pause quantum says.

This is why flow control is generally turned off on switches carrying mixed traffic, and why
the modern answer to congestion is per-flow: TCP's own congestion control, and priority
queueing.

## How much this is worth, honestly

`Plausible`. The head-of-line blocking is a real and well-understood property of 802.3x, not
a theory, and it is the reason network engineers switched it off on general-purpose links.

Whether your link ever generates pause frames is a different question, and on a home gigabit
connection to a router that is rarely saturated inbound, the answer is usually no. If it never
happens, turning it off changes nothing.

If you have ever seen your ping spike specifically while something else on the PC was
downloading, this is one of the two or three things worth trying, alongside
`services.delivery-optimization` and `update.notify-before-download`.

## Trade-off

Under genuine congestion, packets are now dropped instead of paused. For TCP that is fine and
arguably better -- dropping is the signal congestion control is built around. For anything that
relied on a lossless link it is not.

Two cases where you should leave this alone: iSCSI or FCoE storage, and RDMA. Both assume a
lossless fabric and both are unhappy without it. Neither is likely on a gaming PC.

## Revert

`nos revert network.flow-control-off` restores the exact prior string on every adapter it
changed, including deleting nothing -- absent keywords were never written, so there is
nothing to clean up. Takes effect at the next boot, like the apply.
