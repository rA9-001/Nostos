# Removing Nostos

Open **Settings** — the gear in the top right — and use **Remove Nostos from this PC**.

When it finishes, deleting the folder you ran it from is all that is left to do. That is the
whole promise, and the rest of this page is what stands behind it.

## What it does, in order

1. **Puts your settings back.** Every applied tweak is reverted through the ordinary revert
   path, using the value that was captured before the change was made. This is the same code
   the *Revert everything* button runs — not a special uninstall route that nothing else
   exercises.
2. **Disconnects.** The control pipe is closed and the background refresh stops, because both
   are about to stop existing.
3. **Stops and deletes the background service.** One administrator prompt, and only if the
   service is installed.
4. **Deletes `%ProgramData%\Nostos`** — the journal, the logs, the profiles, `service.json`
   and `settings.json`.
5. **Deletes the per-user renderer cache**, if this build made one.

Then it tells you the one folder that is left and offers to close itself.

## Why the order matters

Reverting has to happen *first*. It needs the service (machine-scope values are written by the
LocalSystem half) and it needs the journal (which holds the original value of everything that
was changed). Both are deleted in step 3 and 4. Doing this the other way round would leave a
machine with every tweak still applied and nothing left that knows what they used to be.

That is also why unticking **Undo the changes Nostos made** is a real decision rather than a
convenience. It leaves your machine tuned, and it deletes the only record of what was tuned. The
panel says so before the second click.

## What it deliberately leaves

**System Restore points.** Nostos asks Windows for one before a risky or reboot-requiring
batch. Those checkpoints protect everything else on the PC as well — drivers, updates, other
software — and deleting somebody's recovery history to tidy up after an uninstall would be a
far larger act than removing an app. Windows ages them out on its own schedule.

**Anything Windows reset since.** Some settings are changed back by Windows Update or by a
driver install. Those are already at a Windows-chosen value, and the revert simply confirms it.

## Removal needs one administrator prompt

Only when the service is installed. Deleting a service registration is a Service Control
Manager call no ordinary user can make, and the files the service wrote to `%ProgramData%` as
LocalSystem are not deletable by the account that ran the app.

Declining the prompt stops removal and says so. Note that by then the tweaks have already been
undone — the two are separate steps, and the second one is the one that asks.

The elevated half is `Nostos.Service.exe remove`. It takes no arguments, on purpose: an
elevated process that deletes a directory named by an unelevated caller is an arbitrary-delete
primitive wearing a helpful hat. The only path it touches is the one it computes for itself.

## A portable copy

A single-file `Nostos.exe` keeps everything — journal, profiles, logs, unpacked renderer — in a
`data` folder beside itself, and never installs a service. Removal there still matters, because
the *tweaks* it applied are on the machine like any others: undo them, then delete the folder.

Some files inside `data\runtime` are the renderer the running app is drawing with, so they
cannot be deleted while it is open. They are inside the folder you are about to delete anyway,
which is why they are not reported as leftovers.

## Doing it by hand

If the app will not start:

```powershell
nos revert --all                     # put the settings back first
Nostos.Service.exe remove            # elevated: service and %ProgramData%\Nostos
```

Or, entirely manually, from an elevated prompt:

```powershell
sc.exe stop Nostos
sc.exe delete Nostos
Remove-Item -Recurse -Force $env:ProgramData\Nostos
Remove-Item -Recurse -Force $env:LocalAppData\Nostos   # only if it exists
```

Then delete the folder the app runs from. Doing it this way skips the revert, so whatever was
applied stays applied — with the journal gone, `journal.jsonl` is worth copying somewhere first
if you might want to know what those values were.

## What is left afterwards

Nothing, outside the folder you delete. If something could not be removed — a log file held
open by a process that has not exited, most often — the panel names the exact path rather than
claiming success.
