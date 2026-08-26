# shell.visual-effects

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.
## What it changes

| Key | Value | To |
| --- | --- | --- |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects` | `VisualFXSetting` (REG_DWORD) | `2` |
| `HKCU\Control Panel\Desktop\WindowMetrics` | `MinAnimate` (REG_SZ) | `0` |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `TaskbarAnimations` (REG_DWORD) | `0` |

`VisualFXSetting = 2` is the "Adjust for best performance" radio button in the Performance
Options dialog. `MinAnimate` is the minimise and maximise animation. `TaskbarAnimations` is the
slide-and-fade on taskbar buttons.

## Mechanism

Every one of these is drawn by the Desktop Window Manager, on the GPU, on the desktop. While a
game has exclusive fullscreen, none of it is happening and this tweak is worth exactly nothing.

It is worth something in the cases where the desktop is still being composed:

- **Alt-tabbing.** The animation runs at the moment you are switching, which is the moment you
  care about switching quickly.
- **Borderless windowed**, where the desktop is composed behind and around the game the whole
  time.
- **Integrated graphics**, where DWM and the game share one modest GPU and one memory bus.

## Why "Plausible" and not "Measured"

The work is real and shows up in a GPU capture. What has not been measured, here or anywhere
this repo can point at, is a frametime delta in a game with these three off versus on. Nothing
about it is disputed; it is simply unquantified, and small.

This is a small entry in the **Performance** category rather than a headline one, and it is filed there
because the mechanism is straightforwardly "give the GPU back some work".

## Trade-off

Purely cosmetic, and reversible in one click. Windows becomes abrupt: windows appear and
disappear rather than growing and shrinking. Plenty of people prefer it that way and set it on
machines with performance to spare.

## Revert

`nos revert shell.visual-effects` restores all three previous values, including deleting any
that did not exist before. **User-scoped**, so run it as the signed-in user.
