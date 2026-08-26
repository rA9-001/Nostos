# graphics.fullscreen-optimizations-off

**Group:** Gaming · **Improves:** Input Lag & Aim · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Shortens or steadies the path from your mouse and keyboard to the screen, so the same movement always produces the same result.

## What it changes

`HKCU\System\GameConfigStore`

| Value | Type | Set to |
| --- | --- | --- |
| `GameDVR_FSEBehaviorMode` | REG_DWORD | `2` |
| `GameDVR_HonorUserFSEBehaviorMode` | REG_DWORD | `1` |
| `GameDVR_DXGIHonorFSEWindowsCompatible` | REG_DWORD | `1` |
| `GameDVR_EFSEFeatureFlags` | REG_DWORD | `0` |

All four are set together because they do not work apart: the `Honor` flags are what tell
Windows to respect the behaviour mode at all, and setting the mode without them changes nothing.
This is a common way the tweak is copied around half-finished.

## Mechanism

When a game asks DXGI for exclusive fullscreen, modern Windows does not necessarily give it.
Fullscreen Optimizations intercepts the request and runs the game as a borderless window that
happens to cover the screen, with the Desktop Window Manager still in the present path.

That is done for good reasons -- instant alt-tab, overlays that work, no mode switch on a
multi-monitor setup -- and it costs something. With the compositor involved, a finished frame is
handed to DWM, which composes it and presents it, rather than the game's back buffer being
flipped to the display directly. That is at minimum an extra copy and, depending on timing, an
extra frame of latency.

Turning it off returns games that ask for exclusive fullscreen to a real flip chain.

## What actually changes for you, honestly

**Probably less than you expect**, for two reasons:

- Many modern games never ask for exclusive fullscreen at all. They offer "Fullscreen" in their
  menu and implement it as borderless. This setting cannot give those games something they did
  not request.
- Windows 10 1709 onward implemented *independent flip*, which lets a borderless window bypass
  composition when nothing else needs drawing over it. When independent flip engages, the
  latency difference largely disappears on its own.

Where it still matters: older titles, anything where an overlay keeps independent flip from
engaging, and variable-refresh setups where exclusive fullscreen behaves more predictably.

## Why "Plausible" and not "Measured"

The composition path is documented and the extra copy is real. The size of the win depends on
the game, the driver, whether independent flip was already engaging, and what overlays you run
-- and this repo has measured none of it. A latency capture on a title that genuinely takes
exclusive fullscreen would move this to Measured.

## Trade-off

- **Alt-tab gets slower** on games that now take exclusive fullscreen, and can cause a display
  mode switch -- the black-screen flicker that Fullscreen Optimizations existed to remove.
- **Overlays may stop drawing** in exclusive fullscreen: Discord, Steam, and capture software
  all rely on the compositor being in the path for some of their features.
- Multi-monitor setups are the worst case: a mode switch on the game's display can rearrange
  windows on the others.

If you stream, record, or rely on an overlay, this one is likely to cost you more than it pays.

## Revert

`nos revert graphics.fullscreen-optimizations-off` restores whatever the four values were
before, including deleting the ones that did not exist.

**User-scoped**, so run it as the signed-in user rather than through the LocalSystem service.
