# input.accessibility-shortcuts-off

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Measured · **Scope:** User · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

| Key under `HKCU\Control Panel\Accessibility` | Value | Set to |
| --- | --- | --- |
| `StickyKeys` | `Flags` (REG_SZ) | `506` |
| `Keyboard Response` | `Flags` (REG_SZ) | `122` |
| `ToggleKeys` | `Flags` (REG_SZ) | `58` |

Windows ships `510`, `126` and `62`.

## Mechanism

Each `Flags` value is a bitfield, and all three tweaks clear exactly one bit: `*_HOTKEYACTIVE`
(`0x4`), the one that lets the keyboard shortcut turn the feature on. That is the whole change -
`510 - 4 = 506`, `126 - 4 = 122`, `62 - 4 = 58`.

Everything else is left where it was. `_CONFIRMHOTKEY` (`0x8`, the "do you want to turn on
Sticky Keys?" dialog) stays set, because with the hotkey inactive there is nothing left for it
to confirm, and clearing it as well would mean that re-enabling the hotkey later silently armed
the feature with no prompt.

A machine that has already been through this - or through Settings - may show a lower number
still, such as `498`. That is `_CONFIRMHOTKEY` cleared too, and revert puts it back exactly.

The shortcuts are:

- **Sticky Keys** - press Shift five times.
- **Filter Keys** - hold right Shift for eight seconds.
- **Toggle Keys** - hold Num Lock for five seconds.

The first two are ordinary gameplay. Tapping Shift repeatedly is how you sprint-cancel; holding
it is how you walk. The dialog appears over the game, plays a sound, and takes focus - and if
Filter Keys actually activates, the keyboard starts ignoring repeated keypresses, which feels
exactly like a failing keyboard.

The features themselves stay available in Settings. Only the accidental activation path is
removed.

## Why "Measured"

Unusually for this catalog, there is nothing to be uncertain about. You can reproduce the
interruption on demand - tap Shift five times - and reproduce its absence after applying this.
The mechanism, the trigger and the result are all directly observable, which is what the rating
requires. No claim is being made about framerate, because none is involved.

Filed under **Interruptions** rather than Input Lag & Aim: it stops something appearing over what
you are doing. Filter Keys does affect input handling, but only once it has been switched on by
accident, which this prevents.

## Trade-off

You lose the keyboard shortcuts. If you rely on Sticky Keys - and people do - this removes the
fast way to turn it on, and you will be going through Settings > Accessibility > Keyboard
instead.

Per-user, so it applies to the account that ran it and not to other accounts or the sign-in
screen.

## Revert

`nos revert input.accessibility-shortcuts-off` restores the three exact strings that were there
before, which matters because these are `REG_SZ` and not numbers - a machine that had already
been altered gets its own value back, not the Windows default.
