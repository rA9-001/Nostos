# shell.transparency-off

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Raises the FPS and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`
`EnableTransparency` (REG_DWORD) -> `0`

Same switch as Settings, Personalisation, Colours, "Transparency effects".

## Mechanism

The acrylic and mica materials behind the taskbar, Start menu, notification flyouts and window
title bars are not a static image. They are a live blur of what is behind them, recomputed by
the Desktop Window Manager as things move. That is per-frame GPU work, done on the desktop, at
the same time your game wants the GPU.

Turning it off replaces the blur with a solid colour, which costs essentially nothing.

## How much this is worth, honestly

**On a discrete GPU running a game in fullscreen: almost nothing.** The desktop is not being
composed while a game has the screen, and a modern card would not notice the work anyway.

It is worth something when:

- You are on **integrated graphics**, where DWM and the game are competing for the same modest
  GPU and the same memory bandwidth.
- You play **borderless windowed** with the desktop still being composed behind and around the
  game.
- You have a **high-refresh monitor**, where DWM's per-frame work happens more often.
- You run a lot of visible desktop chrome alongside the game on a second monitor.

This is filed under Performance because the mechanism is straightforwardly "give the GPU back some
work", which is what that category claims. It is a small entry in that category, not a headline
one.

## Why "Plausible" and not "Measured"

DWM's blur cost is real and measurable in a GPU capture, but this repo has not measured the
delta with a game running, and on the hardware where it matters most (integrated) nobody has
published good numbers either. Nothing here is disputed -- it is just unquantified.

## Trade-off

Purely cosmetic. Windows looks flatter: solid taskbar, solid Start menu, solid flyouts. Some
people prefer it. Nothing stops working, and there is no performance cost in the other
direction.

## Revert

`nos revert shell.transparency-off` restores the previous value, including deleting it if it did
not exist.

**User-scoped**, so run it as the signed-in user rather than through the LocalSystem service.
