# network.energy-efficient-ethernet-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

Turns off every energy-saving keyword the adapter exposes: `*EEE`, `EnableGreenEthernet`,
`AdvancedEEE`, `EEELinkAdvertisement`, `EnableSavePowerNow` and `PowerSavingMode`.

Vendors each spell this differently and a given machine will have one or two of them. Naming all
six costs nothing, because absent keywords are skipped rather than created.

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

Energy Efficient Ethernet (802.3az) lets the PHY drop into Low Power Idle when there is
nothing to send, and wake again when there is. Waking is not instant: the transition is
specified in microseconds, and real implementations vary considerably.

Gaming traffic is exactly the pattern that suffers -- small packets, often, with idle gaps
between them. A link that keeps deciding it is idle keeps paying to wake up.

"Green Ethernet" is a related Realtek feature that also drops link speed on short cables. Its
failure mode is worse than latency: on several Realtek chipsets it is a well-known cause of the
link dropping and renegotiating, which shows up in a game as a two-second freeze and a
disconnect.

## How much this is worth, honestly

`Plausible` for the latency claim and rather stronger than that for the stability one.

The microsecond wake-up cost is real and documented, and, like its neighbours, small next to
the internet leg of your ping. The reason this entry is worth applying is the second half: if
your link has ever dropped and re-negotiated mid-match, EEE or Green Ethernet is one of the
first things to rule out, and turning it off is how you rule it out.

If nothing has ever gone wrong with your link, treat this as a microsecond tweak and expect
nothing visible.

## Trade-off

Slightly higher power draw at the NIC and the switch -- on the order of half a watt per port.
That is the entire cost.

If you deliberately run a low-power always-on machine, this is a real if small regression.

## Revert

`nos revert network.energy-efficient-ethernet-off` restores the exact prior string on every adapter it
changed, including deleting nothing -- absent keywords were never written, so there is
nothing to clean up. Takes effect at the next boot, like the apply.
