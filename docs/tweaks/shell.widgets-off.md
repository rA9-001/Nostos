# shell.widgets-off

**Group:** Windows · **Improves:** Interruptions · **Risk:** Safe · **Evidence:** Plausible · **Scope:** User · **Reboot:** no

> Stops things appearing over what you are doing, stealing focus, or restarting the machine at a moment you did not choose.
## What it changes

`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`
`TaskbarDa` (REG_DWORD) -> `0`

The same switch as right-clicking the taskbar, Taskbar settings, "Widgets". Windows 11 only
(`minBuild` 22000); on Windows 10 the tweak reports itself as not applicable rather than writing
a value that means nothing.

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
