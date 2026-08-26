# shell.notifications-off

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications`
`ToastEnabled` (REG_DWORD) → `0`

## Mechanism

Toasts are the notification popups that slide in from the corner. They are drawn by a shell
surface that has to be composed over whatever is on screen — including a game.

Two things follow from that. In borderless or windowed mode a toast is a composition and repaint
of a layer on top of your game, which is a frame-time cost at the worst possible moment. In true
exclusive fullscreen, presenting shell UI can force a mode transition, which is the "game
minimises itself for a second" behaviour people blame on the game.

Setting `ToastEnabled` to `0` suppresses toast display for this user. Notifications are still
generated and still collect in the notification centre; they just do not pop.

## Relationship to Focus Assist / Do Not Disturb

Windows already has a feature for this, and it is the better first answer: Do Not Disturb turns
on automatically while a game is running, if you leave that rule enabled.

This tweak is the blunter version, for people who want toasts gone entirely and permanently
rather than only while a game has focus. If Do Not Disturb's automatic rules are working for
you, you do not need this.

## Why "Plausible" and not "Measured"

The mechanism is real, and the exclusive-fullscreen mode-switch case is well known. But how
often it costs you anything depends on how many notifications you get, which this repo cannot
measure for you. If you get none during a session, this changes nothing.

## Trade-off

Blunt by design. You stop seeing **all** toasts, including ones you may want: calls, messages,
"your download finished", and security prompts that arrive as notifications rather than dialogs.
Nothing is lost — the notification centre still has them — but nothing interrupts you either.

If that is too broad, use Do Not Disturb with the automatic gaming rule instead and leave this
tweak alone.

## Revert

`nos revert shell.notifications-off` restores the prior value, deleting it if it did not exist.

**User-scoped**, so run it as the signed-in user rather than through the LocalSystem service.
