# shell.suggestions-off

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.
## What it changes

All under `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager`, all REG_DWORD,
all set to `0`:

| Value | What it controls |
| --- | --- |
| `SystemPaneSuggestionsEnabled` | "Suggested" entries in the Start menu |
| `SilentInstalledAppsEnabled` | Windows installing promoted apps on its own |
| `SubscribedContent-338389Enabled` | Tips, tricks and suggestions notifications |
| `SubscribedContent-338393Enabled` | Suggested content in the Settings app |
| `SubscribedContent-353694Enabled` | More suggested content in Settings |
| `SubscribedContent-353696Enabled` | And the rest of it |

The numeric names are Microsoft's, not this project's. They line up one-to-one with the
checkboxes under Settings, Personalisation and Settings, System, Notifications.

## Mechanism

`SilentInstalledAppsEnabled` is the interesting one. With it on, Windows will **download and
install applications you did not ask for**, in the background, using disk and bandwidth, and pin
them to Start. That is not a notification setting; it is unattended software installation.

The rest are notifications and Start menu entries, which is straightforward **Interruptions**
territory: a toast has to be dismissed, and dismissing one during a round costs a death.

## Why "Plausible"

The behaviour is documented and the switches are Microsoft's own. What has not been measured is
how often any given machine actually gets one of these, which depends on region, on the build,
and on what Microsoft is promoting that month. The effect is real; the frequency is not
something this repo can put a number on.

## Trade-off

Nothing stops working. You stop being shown app suggestions and product tips.

## Revert

`nos revert shell.suggestions-off` restores all six previous values, including deleting the ones
that did not exist. **User-scoped**, because this is a per-account preference: applying it does
not affect anybody else who signs in to the same PC.
