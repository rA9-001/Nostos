# gamedvr.policy-off

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Measured · **Scope:** Machine · **Reboot:** no

> Raises the FPS and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR`
`AllowGameDVR` (REG_DWORD) → `0`

## Mechanism

Game DVR's background recording keeps a rolling buffer of recent gameplay so the "record that"
shortcut can save the last 30 seconds. Maintaining it costs GPU encoder time, CPU, and a
continuous write stream to disk for as long as a game is running.

This is the machine-wide policy half. Pair it with [gamedvr.user-capture-off](gamedvr.user-capture-off.md)
for the per-user half.

## Why "Measured"

Unlike most entries in this catalog, this one has a mechanism that is straightforwardly
observable: the background recorder is a real workload that shows up in GPU encoder utilisation
and disk I/O, and turning it off removes that workload. It is also Microsoft's own documented
policy setting.

## Trade-off

You lose the "record the last 30 seconds" feature. Manual capture through other software is
unaffected.

## Revert

`nos revert gamedvr.policy-off`.
