# gpu.mpo-off

**Group:** Gaming · **Improves:** Crashes & Freezes · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Fixes a specific fault that shows up while playing: driver timeouts, black screens, flicker. Repairs a broken machine rather than making a working one faster.

## What it changes

`HKLM\SOFTWARE\Microsoft\Windows\Dwm`
`OverlayTestMode` (REG_DWORD) → `5`

## Mechanism

Multi-Plane Overlay lets the Desktop Window Manager hand several independent surfaces — a
fullscreen video, a game, the desktop behind them — straight to the display hardware as
separate planes, and have the scanout engine compose them. Done well, it saves a composition
pass and some power.

Done badly, it produces a specific and recognisable set of bugs: flickering or black flashes
when a video is playing, brief black screens when alt-tabbing, stutter in borderless windowed
mode, and taskbar or cursor corruption. These are driver and panel dependent, and were common
enough that both Microsoft and NVIDIA published `OverlayTestMode = 5` as the workaround.

The value forces DWM off the overlay path and back to normal composition.

## Read this before applying it

**This is a bug workaround, not an optimisation.** If you are not seeing flickering, black
flashes or borderless stutter, this will do nothing for you except cost a little GPU power on
the composition path it re-enables.

It is in the catalog because when you *are* hitting the bug it is genuinely the fix, and the
usual advice is to paste a registry command from a forum post with no record of what the value
was before.

## Why "Plausible" and not "Measured"

The mechanism is real and the workaround is vendor-published. But
whether it helps is entirely a property of your driver, GPU and monitor combination, and this
repo has no frametime data. If you have a machine that reproduces the flicker and you can
capture before and after, that is a very welcome pull request.

## Trade-off

Composition moves back onto the GPU, which costs a small amount of power and, on a weak
integrated GPU, can slightly reduce desktop smoothness with multiple high-refresh monitors.
Hardware-accelerated video playback may use marginally more power.

Needs a reboot: DWM reads this at start. Because it needs a reboot, a System Restore point is
taken before it is applied. Nothing reverts it for you — if the desktop comes back wrong, boot
into safe mode and run `nos revert gpu.mpo-off`, or roll back to that restore point.

## Revert

`nos revert gpu.mpo-off`, then reboot. Revert deletes the value if it did not exist before,
which is the usual case.
