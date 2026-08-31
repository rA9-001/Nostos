# The Startup tab

Everything Windows launches when you sign in, in one list, with a switch on each row.

On a gaming PC this is usually the largest background load on the machine and the one nothing
in the tweak catalog can touch: Razer Synapse, an EA launcher, a Discord updater, an NVIDIA tray
icon. Between them they outweigh anything a registry tweak reclaims.

## Why it is not a tweak

Every other change this program makes is a `ITweak` with a docs page, a risk rating and an
evidence claim. Startup entries are none of those, and forcing them into that shape would have
been dishonest, because **the catalog cannot know what is installed on your machine**. There is
no defensible risk rating for "Razer Synapse": it is essential if you have a Razer mouse whose
DPI you set in software, and pure background load if you do not. That is a fact about you.

So the tab shows what is actually there and gets out of the way. It makes no claim about frames.

## Nothing is deleted

Switching a row off writes the same record Task Manager's Startup tab writes:

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
HKLM\SOFTWARE\...\StartupApproved\Run32
HKCU\SOFTWARE\...\StartupApproved\StartupFolder
```

The Run value or the shortcut itself is never touched. This buys three things:

- **Turning it back on restores exactly what was there**, because it was never removed.
- **Task Manager and Nostos agree.** Change it in either one and the other shows the change.
  Two tools with two private notions of "disabled" is how a machine ends up in a state neither
  of them can explain.
- **Uninstalling Nostos leaves nothing behind** that Windows cannot manage on its own.

The alternative — delete the Run value, remember it in our own journal — is what most debloaters
do, and it is why uninstalling one of them can leave a machine that has quietly forgotten how to
start its own audio driver.

### The approval byte is a bitmask, not an enum

Worth writing down, because getting it wrong produces a list that looks right:

| First byte | Meaning |
|---|---|
| `0x02` | enabled, never touched |
| `0x06` | enabled, after being switched back on |
| `0x03` | disabled |
| `0x07` | disabled, with the upper bits set to something |

**Bit 0 is the disabled flag.** Reading the byte as a value — "2 means on, 3 means off" — works
on most machines and then silently reports one entry backwards on a machine that has an `0x06`
or an `0x07` in it. The machine this was developed on has an `0x07`: Windows Security's tray
icon, switched off.

Writing follows from the same fact. Nostos starts from whatever bytes are already there and
flips bit 0, rather than writing a constant, so the upper bits survive a round trip. Verified:
`07` → enable → `06` → disable → `07`.

## What it lists

| Source | Where |
|---|---|
| Machine-wide Run | `HKLM\...\CurrentVersion\Run` |
| Machine-wide Run, 32-bit | the same key as a 32-bit program sees it |
| Per-user Run | `HKCU\...\CurrentVersion\Run` |
| All-users Startup folder | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup` |
| Your Startup folder | `%AppData%\Microsoft\Windows\Start Menu\Programs\Startup` |

**Scheduled tasks are deliberately not here yet.** Plenty of software starts itself from a
logon-triggered scheduled task, so the list is not complete without them — but they are switched
through a different mechanism (the Task Scheduler API, not `StartupApproved`), and shipping half
of that would have meant a tab where some switches behave one way and some another.

## Icons

Each row carries the program's own icon, read from the executable with `SHGetFileInfo` and
decoded straight out of the icon's bitmaps. It is the fastest way to recognise a program, and
the reason this list reads better than a column of registry value names.

Two cases are handled that a naive version gets wrong:

- **Icons older than 32-bit colour** carry no alpha channel and come back fully transparent —
  an invisible icon rather than a missing one, which looks like a bug in the list. The shape is
  taken from the icon's AND mask instead.
- **Store apps** launch through a zero-length reparse point under `WindowsApps` that the shell
  cannot open on the file's behalf, so it has no icon to give. The generic icon for the file
  type is used instead. Teams is exactly this case.

## Who does the writing

The same split as the tweaks, decided per entry:

- **Per-user entries** are written by the app, in your own session. `HKCU` inside the
  LocalSystem service is *SYSTEM's* hive — the write would succeed and change nothing you would
  ever see.
- **Machine-wide entries** go to the service, which is the only part of the program that can
  write `HKLM`.

The pipe carries "switch the entry called `machine-run:Portmaster`", never a registry path and a
payload. The service resolves that id against the live machine and refuses anything that is not
already a startup entry, so an unprivileged caller cannot use this to reach the rest of the
registry.

If the write is refused — no service installed, and the app not elevated — the row stays where it
was and the reason appears above the list. **The row never shows a state the machine does not
have.**

## It is in the History tab, but not in `revert --all`

Every switch is recorded, so the History tab stays a complete account of what this program did:

```
▼  Startup off — KeePassXC
   It no longer runs when you sign in. Switch it back on in the Startup tab;
   nothing was deleted, so it is still listed there.
   KeePassXC → off
```

All three places that can switch an entry — the window, the service, the CLI — write the same
line, so the tab tells one story wherever the switch was flicked.

**The line is a committed change with no preceding intent**, and that is load-bearing. The
outstanding set that `nos revert --all` works from is built out of intents carrying a snapshot,
so a committed-only line is visible in the history and is never something revert goes looking
for. Without that, undoing an unrelated tweak months later would silently turn Razer Synapse
back on.

There is no snapshot because there is nothing to snapshot. Unlike a registry value, where "what
was it before?" genuinely needs a record, the prior state here is one bit, it is visible in Task
Manager, and undoing it is one click in the tab that did it.

## On the command line

```
nos startup                              # list, with ids
nos startup disable user-run:Steam
nos startup enable machine-run:Portmaster   # needs an elevated terminal
```

The list is exactly what the window shows. Machine-wide entries need elevation and say so
plainly rather than failing with a permissions error.
