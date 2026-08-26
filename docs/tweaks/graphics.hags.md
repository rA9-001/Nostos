# graphics.hags

**Group:** Gaming · **Improves:** Input Lag & Aim · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Shortens or steadies the path from your mouse and keyboard to the screen, so the same movement always produces the same result.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers`
`HwSchMode` (REG_DWORD) → `2` (`1` = off, `2` = on)

Requires Windows 10 2004 (build 19041) or later, and a GPU driver that supports WDDM 2.7
scheduling. Equivalent to the "Hardware-accelerated GPU scheduling" toggle in Settings.

## Mechanism

Moves the GPU work-submission scheduler from a CPU-side kernel thread onto a dedicated
scheduling processor on the GPU, removing a CPU round trip from every submission.

## Why this is filed under Input Lag & Aim and not FPS

Most people meet HAGS expecting frames, and the framerate case is the weak one: independent
benchmark runs across a range of hardware land somewhere between "no change" and "within noise",
occasionally negative. Filing it under Input Lag & Aim would be selling a claim the measurements do not
support.

The latency case is the one with a mechanism behind it — one fewer CPU round trip per
submission — and it is what Microsoft's own description of the feature is about. That is a
smaller promise, and it is the one this tweak can actually keep.

## Why "Plausible", and why "Moderate" risk

Results are genuinely hardware- and driver-dependent: measurable latency wins on some
configurations, regressions or instability on others, and this has changed repeatedly across
driver releases. Because a bad interaction shows up as a display failure — the worst case for
recovery — this tweak requires a reboot, and a System Restore point is taken before it is
applied.

## If the machine comes back broken

**Nothing undoes this for you.** Read that before you apply it: if the machine comes back with
no picture, it stays that way until you act.

In order of preference:

1. Boot into **safe mode** and run `nos revert graphics.hags`, then reboot. Safe mode uses the
   basic display driver, so you will have a picture.
2. If you cannot get that far, use the restore point taken before the change. Windows Recovery
   Environment appears on its own after two failed boots, or hold Shift while clicking Restart.
3. `nos revert --all` from safe mode is the blunt version, and undoes everything this program
   has ever applied. It always works, because the prior values were journaled before the change
   was made.

## Revert

`nos revert graphics.hags`, then reboot.
