# shell.widgets-off

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.

## What it changes

`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`
`TaskbarDa` (REG_DWORD) -> `0`

The same switch as right-clicking the taskbar, Taskbar settings, "Widgets". Windows 11 only
(`minBuild` 22000); on Windows 10 the tweak reports itself as not applicable rather than writing
a value that means nothing.

## When this is not applicable

Besides Windows 10, this tweak stands down when Widgets has been turned off machine-wide by
Group Policy:

`HKLM\SOFTWARE\Policies\Microsoft\Dsh`
`AllowNewsAndInterests` (REG_DWORD) = `0`

— *Computer Configuration > Administrative Templates > Windows Components > Widgets > Allow
widgets*, and the value some "debloat" scripts and Windows editions set for you.

While that policy is in force **Windows refuses every write to `TaskbarDa`**, including from an
elevated process and including from SYSTEM. It is worth being precise about why, because it
looks like a permissions bug and is not one: the `Advanced` key's ACL grants the user full
control, the key opens for writing, and creating any *other* value in it succeeds. What refuses
is a kernel registry callback that rejects that one value name, because the policy owns the
setting. The failure surfaces as `UnauthorizedAccessException`, which sends people to look at
elevation, which never helps.

There is nothing to do about it and nothing worth doing: the policy has already achieved what
this tweak is for. The board is gone. If you want the taskbar button back, lift the policy.

## Mechanism

The Widgets board is a web page. It is rendered by **WebView2**, which is Edge, which is
Chromium. With it enabled, `Widgets.exe` and one or more `msedgewebview2.exe` processes sit
resident, fetching news and weather and re-rendering.

It is filed under **Interruptions** rather than FPS for a specific reason: the weather panel
lives at the left edge of the taskbar and **hovering it opens the board**. Coming out of a game
with the pointer near the bottom-left corner is enough to wake a browser engine and slide a
panel across the screen. Removing the button removes the hover target.

## Why "Plausible"

That a Chromium process uses memory and CPU is not in question, and neither is the hover
behaviour. What this repo has not measured is a framerate difference with it on and off, and
during exclusive fullscreen there is no reason to expect one: the process is idle when nothing
is asking it to render.

## Trade-off

You lose the widgets board. If you use it for the weather, do not do this.

## Revert

`nos revert shell.widgets-off` restores the previous value, or removes it if it was never set.
**User-scoped.** Explorer picks the change up within a few seconds.
