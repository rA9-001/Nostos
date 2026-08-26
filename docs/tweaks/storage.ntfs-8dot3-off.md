# storage.ntfs-8dot3-off

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`
`NtfsDisable8dot3NameCreation` (REG_DWORD) -> `1`

Windows 10 and 11 ship `2`, which means "decide per volume" - and in practice means short names
are still created on the system volume.

## Mechanism

Every file whose name does not fit the 8.3 MS-DOS format gets a second, hidden name generated
for it: `Program Files` also answers to `PROGRA~1`. Creating one is not free. NTFS has to scan
the directory for collisions before it can pick a suffix, and in a directory that already holds
many similar long names that scan gets expensive - the pathological case, a folder with
thousands of files sharing a prefix, is measurably slow to write into.

The generated name also occupies an extra entry in the directory index, so every such directory
is larger and slower to enumerate than it needs to be.

Setting `1` disables generation on all volumes. Existing short names are **not** removed; this
only stops new ones.

## How much this is worth, honestly

`Plausible`. The cost is real and documented, and Microsoft's own guidance for file servers is to
turn it off. What is not established is that a gaming machine ever hits the case where it
matters - you would need a game or a tool that creates very large numbers of long-named files in
one directory, and most do not.

Filed under **Background & Cleanup** because the effect is on file creation, not on anything a
running game does.

## Trade-off

**Software from before about 2001 can break**, and so can a surprising amount of installer
tooling that still shells out through short paths. The specific failure is a program that
hard-codes a `PROGRA~1`-style path, or an installer that generates one and finds nothing there.

The other failure is subtler: if anything already stored a short name - an old registry entry, a
shortcut, an uninstaller - that name keeps working, because existing names survive. It is only
newly created files that lack one. So problems appear later, when something is reinstalled.

Needs a reboot.

## Revert

`nos revert storage.ntfs-8dot3-off`, then reboot. Short names created while it was on stay
created; there is nothing to undo about them.
