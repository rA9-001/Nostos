# The Cleanup tab

Space taken by files that programs rebuild on their own: shader caches, scratch directories,
launcher caches, crash reports.

On the machine this was built on that is **9.3 GB**, of which 4.5 GB is one folder — the NVIDIA
driver's compiled shaders.

## The rule that makes it safe

**A fixed list of named places, never a search for folders that look like caches.**

That distinction is the whole safety argument, and it is not theoretical. On this machine,
`Steam\steamapps\downloading` held **172 GB**: a paused update for a game, sitting in a folder
whose name and contents look exactly like disposable staging that nobody wants. A cleaner that
matched on shape rather than on knowledge would have taken it, and the owner would have found out
by watching Steam download 172 GB again.

So every location is written down, with what it is and what you lose. There are 22 of them.

### Why these are deleted rather than recycled

Everywhere else in this program, nothing is deleted: folders go to the Recycle Bin, registry keys
are exported before they are removed. Here they are deleted outright, and the list itself is what
earns that:

**Everything on it regenerates.** A shader cache is rebuilt on the next launch, a thumbnail
database on the next folder view, a log on the next run. There is nothing to undo, because
nothing is lost that the machine will not make again.

Recycling them would also be worse than useless: it frees no space until the bin is emptied, and
the bin is where this program keeps the undo for uninstalls. Filling it with four gigabytes of
dead shaders makes that undo harder to use.

The rule follows from that, and it is the one to hold on to when adding a location:
**if it does not regenerate, it does not belong on this list.**

## What it will not touch

Written down rather than merely left out, so that a future change has to argue with the reason
rather than simply not think of it:

| Refused | Why |
|---|---|
| `steamapps\downloading` | a paused or interrupted download. 172 GB of one here. |
| `steamapps\workshop` | mod content somebody chose to install. |
| `%USERPROFILE%\Downloads` | files a person put there on purpose. Every cleaner that offers this is wrong. |
| `C:\Windows\Prefetch` | Windows uses it to start programs faster and rebuilds it slowly. A cost with no benefit, and the most commonly recommended bad idea in this space. |
| `C:\Windows.old` | the rollback path to the previous Windows build. Windows removes it on its own schedule. |
| `pagefile.sys`, `hiberfil.sys` | not caches. Their size is a power and memory setting, and changing it belongs in a tweak that says so. |
| browser profiles | cookies, sessions and saved passwords live beside the cache. |
| the Recycle Bin | it is the undo for this program's own removals. |

## The rules inside a sweep

- **The folder itself survives.** Only its contents go. Several programs create their cache
  directory once at install time and assume it from then on; a tidier machine that loses one of
  those is not tidier.
- **Links are never followed.** A junction or symbolic link under Temp can point anywhere on the
  machine, and following one turns "empty a cache" into "delete my documents". The directory walk
  is written by hand for exactly this reason — the framework's recursive enumeration follows
  them. Every file is also checked to be under the target after its full path is resolved, as a
  backstop rather than a duplicate.
- **A file in use is skipped, not failed.** That is the normal case in Temp. It is counted and
  reported as a number.
- **Scratch directories have an age rule.** Only files untouched for a day are taken, because
  what is in Temp right now is very likely an installer's working directory. On this machine that
  holds back 5,540 of 17,678 files — which is why the row says how many, next to the size. A
  total that looks far too small next to what Explorer reports needs its explanation standing
  beside it.
- **Shader caches have no age rule**, deliberately. They are held open or they are not, and
  making somebody wait a day would defeat the reason they came: clearing one is the standard fix
  for stutter after a driver update.

## Elevation

Four locations are outside the user's profile — Windows' own Temp, the Windows Update download
cache, the update sharing cache, and machine-wide error reports. Nostos never elevates itself, so
those are **measured either way and skipped with a reason** when it is not running elevated.
Measuring them regardless is deliberate: "you would get 2 GB back if you ran this elevated" is
worth knowing, and a row that says only "blocked" invites somebody to elevate for nothing.

That measurement is now stated where it can be acted on: the page carries a line saying how much
more an elevated launch would reach, and each affected row carries a **Needs administrator**
badge from the moment it is drawn.

**The row keeps its Clean button.** The first version removed it, which taught the wrong lesson:
what a missing button says is that this program cannot clean Windows' own temp folder, not that
this copy is not elevated. Pressing it now says what would make it work, and the row goes on
offering it, because the thing that would change the answer is starting the program differently
rather than anything on this page.

This is the rule the Tweaks, Profiles, Startup and Apps tabs follow too — *when the answer is
knowable in advance, the control stays and says what it needs* — and every sentence comes from
one place, so they cannot drift apart.

The Win32 layer reports the fact, `CleanupBlock.NeedsAdmin`, rather than a sentence. It has to:
the same code runs inside a service that has no user and therefore no language, and the wording
has to say what to do about it, which only the window can decide.

Routing these through the service is possible but has not been done. Unlike a tweak, this is
walking a user's own directories, which is not work a LocalSystem service should be doing on
somebody's behalf.
