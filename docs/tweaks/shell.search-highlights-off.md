# shell.search-highlights-off

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.
## What it changes

`HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings`
`IsDynamicSearchBoxEnabled` (REG_DWORD) -> `0`

The same switch as right-clicking the taskbar search box and turning off "Search highlights".
Windows 11 only (`minBuild` 22000).

## Mechanism

Search highlights are the illustrations, holidays and "trending" items that appear in the search
box and inside the search panel. They are **fetched from the internet on a schedule**, cached
locally, and animated between in the taskbar.

This is filed under **Interruptions** because the visible behaviour is a taskbar element that
changes on its own while you are doing something else, and because opening the search panel
pulls down web content you did not ask for at a moment you did not choose.

## Why "Plausible"

Periodic network fetches and an animating taskbar element are both observable. Neither has been
measured against frametimes here, and while a game is fullscreen the taskbar is not being drawn
at all.

## Trade-off

The search box goes plain and the search panel stops showing web suggestions. Local search -
apps, files, settings - is completely unaffected.

## Revert

`nos revert shell.search-highlights-off` restores the previous value, or removes it if it was
never set. **User-scoped.**
