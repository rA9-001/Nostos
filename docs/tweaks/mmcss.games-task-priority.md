# mmcss.games-task-priority

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games`

| Value | Type | Set to |
| --- | --- | --- |
| `Priority` | REG_DWORD | `6` |
| `Scheduling Category` | REG_SZ | `High` |
| `SFIO Priority` | REG_SZ | `High` |

## How much this is worth, honestly

This is one of the most-copied blocks in Windows tweaking guides, and it only affects threads
that call `AvSetMmThreadCharacteristics` with the task name `"Games"`. Very few games do;
audio engines register as `"Audio"` or `"Pro Audio"`, and most game render threads register
with nothing at all.

On a machine where no thread registers under `Games`, this changes precisely nothing. It is
kept in the catalog because it is harmless and because people will otherwise apply it by hand
with no record of the prior values — at least this way it is journaled and revertible.

It is hidden from `nos list` unless you pass `--all`.

## Revert

`nos revert mmcss.games-task-priority`.
