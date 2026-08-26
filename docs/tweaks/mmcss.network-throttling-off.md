# mmcss.network-throttling-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.

## What it changes

`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile`
`NetworkThrottlingIndex` (REG_DWORD) → `0xFFFFFFFF`

Default is `10`.

## Mechanism

While an MMCSS multimedia task is active, Windows limits the machine to roughly
`NetworkThrottlingIndex` × 1000 packets per second so that network interrupt processing cannot
starve audio playback. `0xFFFFFFFF` disables the limiter.

The default cap of ~10,000 packets/s is generous for a game, but it is reached by machines
that are simultaneously streaming, downloading, or on a busy LAN.

## Why "Plausible"

The throttle is documented and real. Whether any given machine ever hits the cap is the open
question, and unhit caps make no difference. Measure before believing.

## Revert

`nos revert mmcss.network-throttling-off`.
