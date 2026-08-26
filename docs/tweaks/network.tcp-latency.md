# network.tcp-latency

**Group:** Gaming · **Improves:** Ping · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

For **every** interface under
`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}`:

| Value | Type | Set to |
| --- | --- | --- |
| `TcpAckFrequency` | REG_DWORD | `1` |
| `TCPNoDelay` | REG_DWORD | `1` |

Neither value exists by default. Revert deletes the ones this tool created rather than writing
a `0`, so the stack goes back to its own defaults instead of being pinned to them.

## Why this one is code and not a catalog entry

Every other registry tweak here is a JSON object. This one cannot be: the keys live under a
per-adapter GUID that is different on every machine and changes when hardware comes and goes,
so the value list has to be discovered at runtime. It lives in
`src/Nostos.Tweaks/Native/TcpLatencyTweak.cs` and reuses the same capture and restore
code as the declarative tweaks.

It applies to **all** interfaces, not just the one currently carrying traffic. Targeting "the
active adapter" sounds tidier and is worse: moving from Wi-Fi to Ethernet, or plugging into a
dock, would leave the new adapter untouched while the tweak still reported itself as applied.

Partial application counts as **not** applied. If one adapter has the values and another does
not, `nos status` reports it as off and tells you how many of how many are set.

## Mechanism

Two separate delays, both of which exist for good reasons on general-purpose networks:

**Delayed acknowledgement.** TCP does not acknowledge every segment immediately. It waits for a
second segment to arrive so it can acknowledge both at once, and if none comes, it waits out a
timer — up to 200 ms — before sending a lone ACK. `TcpAckFrequency = 1` makes it acknowledge
every segment immediately.

**Nagle's algorithm.** When an application writes small amounts of data repeatedly, TCP holds
each write until the previous data is acknowledged, then sends the accumulated bytes as one
segment. This avoids flooding a network with 1-byte packets wrapped in 40 bytes of header.
`TCPNoDelay = 1` disables it, so each write goes out on its own.

Together these interact badly for exactly one traffic pattern: **small, frequent, latency-
sensitive writes**. That is a game sending position and input updates. Nagle holds your input
waiting for an ACK; delayed ACK holds the ACK waiting for more data. The result can be an added
delay of up to the delayed-ACK timer on traffic where every millisecond is visible.

## Read this before applying it: it may not apply to your game

**Most modern multiplayer games use UDP, not TCP.** UDP has neither Nagle nor delayed ACK, so
for those titles this tweak changes precisely nothing about your gameplay latency.

It matters for games whose real-time traffic is TCP — a category that includes a good number of
MMOs and older or browser-based titles — and for anything else on the machine using small TCP
writes.

If you do not know which your game uses, assume UDP and assume this will do nothing. That is
the honest default expectation.

## Why "Plausible" and not "Measured"

The mechanism is textbook TCP behaviour, thoroughly documented, and the interaction is a
well-known one — the mechanism is not in doubt. What is unmeasured is the effect on any particular game,
because it depends on whether that game uses TCP at all, and this repo has collected no packet
captures. A before-and-after latency measurement on a TCP-based title would be a very welcome
pull request, and would move this to Measured.

## Trade-off

- **More packets.** Acknowledging every segment instead of every second one roughly doubles ACK
  count on busy connections. On a saturated or metered link that is measurable overhead, and on
  a weak router it is more work.
- **Bulk transfers can get slightly slower**, since small writes are no longer coalesced. Large
  streaming downloads are mostly unaffected — they already send full segments.
- It affects **all TCP traffic on the machine**, not just games. That is why this is rated
  Moderate rather than Safe.

## Revert

`nos revert network.tcp-latency`, then reboot. Existing connections keep the behaviour they were
established with either way, which is also why applying it asks for a reboot.
