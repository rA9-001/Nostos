# input.mouse-acceleration-off

**Group:** Gaming · **Improves:** Input Lag & Aim · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Shortens or steadies the path from your mouse and keyboard to the screen, so the same movement always produces the same result.

## What it changes

`HKCU\Control Panel\Mouse`

| Value | Type | Set to |
| --- | --- | --- |
| `MouseSpeed` | REG_SZ | `0` |
| `MouseThreshold1` | REG_SZ | `0` |
| `MouseThreshold2` | REG_SZ | `0` |

These three are exactly what the **Enhance pointer precision** checkbox writes
(Settings → Bluetooth & devices → Mouse → Additional mouse settings → Pointer Options).

Note the type: they are strings, not DWORDs. A tool that writes DWORDs here produces values
Windows ignores, and the checkbox stays ticked.

## Mechanism

With acceleration on, Windows scales pointer movement by how fast you moved: `MouseThreshold1`
and `MouseThreshold2` are the speed thresholds, and `MouseSpeed` selects the multiplier applied
past them. The same physical 10 cm sweep produces a different on-screen distance depending on
how quickly you made it.

Zeroing all three makes the mapping linear — the pointer moves a fixed multiple of the counts
the mouse reports, every time.

This does not change `MouseSensitivity` (the 1–20 pointer speed slider), which is a plain
multiplier and is left alone. Only the *velocity-dependent* part is removed.

## Why this is not a performance tweak

It will not gain you a frame. What it changes is **consistency**: with acceleration on, muscle
memory for a 180° turn is only correct at the speed you trained it at. Every competitive
shooter's own settings guide recommends turning it off, and most games that take mouse input
through raw input already bypass it — this makes the desktop and any game that does not use raw
input behave the same way.

## Why "Plausible" and not "Measured"

The mechanism is documented and the effect is deterministic, but this repo has collected no
data, and "does it help you aim" is a question about a person rather than a machine. It is
trivially verifiable if you want to: turn it off, then move the mouse slowly and quickly across
the same mousepad distance and watch whether the pointer lands in the same place.

## Trade-off

Pointer travel across a large or multi-monitor desktop takes more physical movement, because
you can no longer "flick" the cursor across the screen. Some people find the desktop worse and
the game better; the setting is per-user and instant, so try it.

## Revert

`nos revert input.mouse-acceleration-off` restores all three prior values, including deleting
any it created.

This is a **user-scoped** tweak. The LocalSystem service refuses it, because writing `HKCU` as
SYSTEM would write to SYSTEM's own hive and leave your setting untouched. Run it from the CLI
or the app as the signed-in user.
