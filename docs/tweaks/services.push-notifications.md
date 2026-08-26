# services.push-notifications

**Group:** Windows · **Improves:** Interruptions · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

The start type of the **`WpnService`** service - Windows Push Notifications System Service - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

Windows keeps a persistent connection to Microsoft's notification servers so that a toast
raised on your phone, in Teams, or by a Store app can be pushed to this machine and drawn by
the shell. `WpnService` owns that connection and the local notification database.

This is the transport, not the policy. `shell.notifications-off` decides whether toasts are
shown; this decides whether they arrive at all.

## How much this is worth, honestly

Two things make this worth listing rather than assuming. A toast drawn over an exclusive
fullscreen game costs a mode switch on some driver and display combinations, and a toast drawn
over a borderless one steals focus if you click it by reflex. Neither is a framerate claim.

The connection itself is idle almost all of the time. If you are looking for the entry that
stops interruptions rather than the one that stops a background socket, apply
`shell.notifications-off` first and only come here if something is still getting through.

## Trade-off

Store apps, Teams, Outlook and anything else using the modern notification API stop being
able to notify you - including things you may want, like a download finishing or a calendar
alarm. Alarms & Clock in particular relies on this.

Desktop programs that draw their own tray balloons are unaffected.

## What "Manual" and "Disabled" actually mean

`--set start=<option>`, or the radio buttons in the app.

| Option | Start type | What it means |
| --- | --- | --- |
| `manual` | `SERVICE_DEMAND_START` | **Default and recommended.** The service no longer starts at boot, but anything that asks for it can still start it. |
| `disabled` | `SERVICE_DISABLED` | The service cannot start at all. Anything that needs it fails. |

**Manual is a safety net.** If the reasoning on this page turns out to be wrong for your machine,
Manual means the service starts on demand and you never find out there was a problem. Disabled
means whatever needed it fails with an error naming neither the service nor this tool, weeks
after you ran it.

Pick `disabled` only after checking that the service keeps starting itself on Manual and
deciding you would rather it did not.

## What revert does

`nos revert <id>` restores the **exact** start type captured before the change, including the
difference between `Automatic` and `Automatic (Delayed Start)` - two settings the SCM reports
identically and that a "restore defaults" would collapse into one.

The service is not restarted by revert. Its start type says what should happen at the next boot,
and starting a service that was deliberately stopped is a larger intervention than a revert was
asked to make.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
