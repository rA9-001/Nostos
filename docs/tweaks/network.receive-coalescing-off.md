# network.receive-coalescing-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

Turns off the per-adapter receive coalescing keywords on every adapter that has one:
`*RscIPv4`, `*RscIPv6`, `*WdiRscIPv4`, `*WdiRscIPv6` and `*PacketCoalescing`.

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

Receive Segment Coalescing merges several arriving TCP segments from the same flow into one
larger segment before handing it to the stack. Fewer, bigger units means less per-packet work
and noticeably less CPU on a fast link.

It buys that by waiting. The card holds the first segment while it looks for more to merge with
it, and Microsoft's documentation says plainly that RSC should be disabled when running
latency-sensitive traffic.

`*PacketCoalescing` is the Wi-Fi equivalent and is turned off here too.

## How much this is worth, honestly

`Plausible`, with a documented mechanism and an explicit vendor recommendation behind it,
which is more than most ping advice has.

The caveat is the same as its neighbours: the delay is small in absolute terms, and on a
connection with 30 ms of internet in it you are unlikely to see the difference in a ping
number. Where people do report a difference is jitter rather than mean -- coalescing makes
arrival times lumpy by design, and lumpy arrival is what a netcode interpolator has to smooth
over.

## Trade-off

More CPU per received byte, the same trade as interrupt moderation. Bulk download throughput
can drop on very fast links.

**On some machines this tweak cannot reach the setting at all.** Plenty of wired NICs -- the
common Realtek PCIe GbE family among them -- expose no per-adapter RSC keyword, and on those the
only control is the global one:

```
netsh int tcp set global rsc=disabled
```

That is deliberately **not** a tweak in this catalog. `netsh int tcp set global` settings are
applied to the running TCP stack and are not stored anywhere this program can read back, so it
could apply one and then be unable to tell you afterwards whether it was still set, or restore
it exactly on revert. A change this tool cannot prove is a change it should not be making.

Run it yourself if you want it, and remember it is yours to undo (`rsc=enabled`).

## Revert

`nos revert network.receive-coalescing-off` restores the exact prior string on every adapter it
changed, including deleting nothing -- absent keywords were never written, so there is
nothing to clean up. Takes effect at the next boot, like the apply.
