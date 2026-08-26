# network.ipv6-components

**Group:** Gaming · **Improves:** Ping · **Risk:** Risky · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters`
`DisabledComponents` (REG_DWORD)

The value is absent on a clean install, which is equivalent to `0`.

## Mechanism

A bitmask, not a switch. The bits Microsoft documents:

| Bit | Value | Effect |
| --- | --- | --- |
| 0 | `0x01` | Disable all IPv6 tunnel interfaces: Teredo, 6to4, ISATAP |
| 1 | `0x02` | Disable 6to4 |
| 3 | `0x08` | Disable ISATAP |
| 4 | `0x10` | Disable Teredo |
| 5 | `0x20` | Prefer IPv4 over IPv6 in the prefix policy table |
| - | `0xFF` | Disable IPv6 on all interfaces except loopback |

`0x20` is the interesting one and the one this tweak recommends. It does not disable anything;
it reorders the prefix policy table so that when a hostname resolves to both an A and an AAAA
record, the stack tries IPv4 first.

That matters because of how the alternative fails. If a server advertises IPv6 and your path to
it is broken - a common state on consumer connections with partial or misconfigured IPv6 - the
connection attempt has to time out before IPv4 is tried. Happy Eyeballs hides most of this in
browsers; game launchers and update clients frequently do not implement it.

## How much this is worth, honestly

`Plausible` for `prefer-ipv4`. The stall it removes is real and reproducible on an affected
connection, and invisible on a healthy one. If your IPv6 works, this option does nothing for you
except make the ordering explicit.

For the other options the honest answer is that **the ping claim is false**. Turning IPv6 off
does not reduce round-trip time on an IPv4 connection you were already using. The idea that it
does is one of the most durable pieces of folklore in network tweaking, and it survives because
people apply it alongside five other things and attribute the result here.

Microsoft's position is explicit: they do not recommend disabling IPv6, and Windows is not
tested with it off. That is not a formality - components do behave differently.

Needs a reboot; the stack reads this at initialisation.

## Trade-off

`tunnels-off` disables Teredo. Teredo is what Xbox networking and several peer-to-peer
matchmaking systems use to traverse NAT, so party chat and multiplayer for Microsoft Store
titles can stop working, with an error that says nothing about IPv6.

`ipv6-off` additionally breaks: any ISP delivering native IPv6, some VPN clients, some Hyper-V
and container networking, and parts of the Store stack. Home networking discovery gets slower
because it falls back to older protocols.

Both failures are hard to attribute weeks later, which is the main reason this entry is rated
**Risky** and defaults to the option that disables nothing.

## Revert

`nos revert network.ipv6-components`, then reboot. Revert removes the value if it was absent
before, which is not the same as writing `0`.
