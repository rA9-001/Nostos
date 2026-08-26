# update.notify-before-download

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU`

| Value | Set to |
| --- | --- |
| `NoAutoUpdate` (REG_DWORD) | `0` |
| `AUOptions` (REG_DWORD) | `2` |

Neither exists on a clean install.

## Mechanism

`AUOptions` selects Automatic Update behaviour: `2` notify before downloading, `3` download
automatically and notify before installing, `4` download and install on a schedule, `5` let the
local administrator choose. `NoAutoUpdate = 0` keeps the update client enabled - it is set
explicitly so that this reads as "automatic updates are on, and they ask first" rather than "off".

The interruption this addresses is the download, not the install. Windows will start pulling a
multi-gigabyte cumulative update whenever it decides the connection is suitable, and on a
metered-off Ethernet connection it decides that immediately. That download competes for
bandwidth with the game you are in, and on an asymmetric connection its acknowledgement traffic
alone is enough to add jitter.

Pair it with `update.no-auto-restart`, which handles the other half - the reboot - and with
`update.delivery-optimization-off`, which handles the upload.

## How much this is worth, honestly

`Plausible`. That a saturated link raises ping is not in question; whether Windows Update is what
saturated yours on any given evening is. If your connection is fast enough that a background
download is invisible, this changes nothing you can feel.

Filed under **Interruptions** rather than Ping because the reliable effect is on when things
happen, not on latency: you get told, and you decide. Bandwidth is the mechanism, not the
promise.

One caveat worth stating plainly: on **Windows 11 Home** these legacy `AU` policies are only
partially honoured. Home has never respected the full Automatic Update policy set, and Microsoft
has narrowed what it accepts over time. Verify with `nos status update.notify-before-download`
after a reboot and do not assume it took.

## If your machine already has `NoAutoUpdate = 1`

Read this before applying. Some tweak packs set `NoAutoUpdate = 1`, which switches automatic
updating **off entirely** - nothing is checked, downloaded or installed until you open Settings
and press the button.

This tweak writes `0`. It has to: `AUOptions` is only consulted when the update client is
enabled, so leaving `NoAutoUpdate = 1` would make the notify setting dead text.

So on a machine in that state, applying this is a move *towards* updating, not away from it. If
that is not what you want, do not apply it - `nos status update.notify-before-download` shows
what the machine currently says, and revert restores it exactly.

## Trade-off

**Updates now wait for you.** Security updates included. A machine where nobody clicks the
notification is a machine that stops being patched, and that is a considerably worse outcome
than a download during a match.

Set a reminder, or prefer `update.no-auto-restart` on its own if you are not confident you will
act on the prompts.

## Revert

`nos revert update.notify-before-download` removes both values, returning Windows Update to
whatever Settings says.
