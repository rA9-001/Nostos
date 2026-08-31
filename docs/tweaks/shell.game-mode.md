# shell.game-mode

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Raises the FPS and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKCU\Software\Microsoft\GameBar`
`AutoGameModeEnabled` (REG_DWORD)

Same switch as Settings → Gaming → Game Mode.

## Options

`--set state=<option>`, or the radio buttons in the app's detail pane.

| Option | Value | What it means |
| --- | --- | --- |
| `on` | 1 | **Default and recommended.** The Windows default, and Microsoft's own advice. |
| `off` | 0 | Turns it off. Only worth trying against a stutter you have traced to it. |

## Mechanism

Game Mode is Windows' own handling for a foreground game. When it detects one, it:

- **Holds back Windows Update restarts and driver installs.** This is the part nobody disputes,
  and on its own it justifies leaving the feature on. Nothing else in this catalog stops Windows
  rebooting your machine in the middle of a ranked match.
- Reduces the resources given to background work so the game gets more consistent CPU and GPU
  time.

It is not a magic performance switch, and Microsoft has never claimed it was.

## Why this tweak exists if the default is already "on"

Two reasons.

Other "optimizer" tools turn Game Mode **off**, on the strength of a 2019-era reputation for
causing stutter in a few titles. If one of them has been run on this machine, the setting is
already off and nothing tells you. Applying this tweak puts it back and journals the change.

Second, having it in the catalog means the choice is written down with its reasoning, rather
than being a checkbox in Settings you have no opinion about.

This is a tweak whose recommended state is the Windows default, and that is fine — a catalog
that only ever moves away from defaults is a catalog with an agenda.

## Why "Plausible" and not "Measured"

The Windows Update behaviour is documented and certain. The resource-allocation half is real
but its size is not something this repo has measured, and Microsoft has never published numbers
either. The historical stutter reports were real for specific titles at specific times and are
largely stale now, which is exactly the kind of claim that should not be rated higher than
Plausible in either direction.

## Trade-off

Very little. A small number of titles have historically behaved worse with Game Mode on; if you
are chasing a stutter and have ruled out everything else, `--set state=off` is a cheap thing to
test. Do not turn it off speculatively.

## Revert

`nos revert shell.game-mode` restores whatever the value was before, including deleting it if it
did not exist.

**User-scoped**, so run it as the signed-in user rather than through the LocalSystem service.
