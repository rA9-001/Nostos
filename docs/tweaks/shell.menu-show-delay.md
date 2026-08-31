# shell.menu-show-delay

**Group:** Gaming · **Improves:** Input Lag & Aim · **Risk:** Safe · **Evidence:** Measured · **Scope:** User · **Reboot:** no

> Shortens or steadies the path from your mouse and keyboard to the screen, so the same movement always produces the same result.

## What it changes

`HKCU\Control Panel\Desktop`
`MenuShowDelay` (REG_SZ) -> `0`, `100`, or `400`

## Mechanism

When you hover a menu, Windows starts a timer and opens the menu when the timer expires. The
timer is `MenuShowDelay` milliseconds long and defaults to **400**.

It is not waiting for anything. It is not measuring intent, not debouncing, not loading the
menu. It is a pause, added deliberately, back when submenus flying open as you swept past them
was a real annoyance on a small screen.

This is filed under **Input Lag & Aim** because it is delay between an input and a response,
which is what that category is about. It should be said plainly that **this does nothing inside
a game** - no game uses the Windows menu system. What it changes is how the machine feels
everywhere else: Start menu, right-click menus, the menu bar of every desktop application.

## Why "Measured"

Unusually for this catalog, there is nothing to argue about. The value is a delay in
milliseconds, the shell applies exactly that delay before showing a menu, and setting it to zero
removes exactly that many milliseconds. A stopwatch settles it.

## Options

`--set delay=<option>`, or the radio buttons in the app.

| Option | Value | What it means |
| --- | --- | --- |
| `instant` | `0` | **Default.** Menus open the instant the pointer lands. |
| `short` | `100` | Feels immediate, but sweeping across a menu bar does not open everything on the way. |
| `windows-default` | `400` | The shipping value, written explicitly so drift reconciliation has something to compare against. |

## Trade-off

At `0`, dragging the pointer diagonally across a menu bar opens every menu it crosses. If that
reads as noisy, `short` is the setting you want.

## Revert

`nos revert shell.menu-show-delay` puts the previous string back, including removing the value
entirely if it was never set. **User-scoped.** It applies to new menus straight away; an
application that cached the value at start-up needs restarting.
