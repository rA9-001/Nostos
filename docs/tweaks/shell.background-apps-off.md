# shell.background-apps-off

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Raises the FPS and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications`
`GlobalUserDisabled` (REG_DWORD) → `1`

Same switch as Settings → Apps → Installed apps → *Background apps permissions*, applied to
every packaged app at once.

## Mechanism

Packaged (Store/UWP/MSIX) apps can register background tasks that the system runs on their
behalf — polling for mail, refreshing live tiles, checking for updates, syncing. The broker
wakes them on timers and system events whether or not the app is open.

Setting `GlobalUserDisabled` tells the background task broker to stop granting those activations
for this user. The apps still launch and work normally when you open them.

## What this is actually worth

Honestly: not much on a machine with few Store apps, which describes most gaming PCs. The
processes involved are small, and Windows already suppresses much of this under Game Mode.

Where it earns its place is machines that came with a manufacturer's Store apps preinstalled,
where a handful of background tasks wake up during a match and cost you a frame-time spike
rather than average FPS. Spikes are what you notice.

## Why "Plausible" and not "Measured"

The mechanism is documented and the setting is Microsoft's own. What is unmeasured here is the
size of the effect, which depends entirely on which packaged apps you happen to have. On a
clean install with none, it is zero.

## Trade-off

Real, and worth knowing before you apply it:

- **Store-app notifications stop arriving** when the app is closed. If you use the Store version
  of a chat or mail client, it will not tell you about new messages until you open it.
- Live tiles and widgets stop refreshing.
- Store apps stop updating themselves in the background.

Classic desktop programs — Steam, Discord's desktop build, launchers, everything installed from
an `.exe` — are **not affected**. This only governs packaged apps.

## Revert

`nos revert shell.background-apps-off` restores the prior value, deleting it if it did not exist.

This is a **user-scoped** tweak, so the LocalSystem service refuses it: writing `HKCU` as SYSTEM
would land in SYSTEM's hive. Run it as the signed-in user.
