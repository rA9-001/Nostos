# Changelog

Notable changes per release. Versions follow [semantic versioning](https://semver.org): while
the major version is `0`, a minor bump may change behaviour.

## 0.3.0 - 2026-09-06

### Added
- **An Apps tab: everything installed on this PC, and an uninstaller that cleans up after
  itself.** Windows has no single honest list of what is installed — Settings shows Store packages
  and per-user installs, Programs and Features shows machine-wide Win32 and hides Store packages,
  and neither shows what scoop or Chocolatey put there. This merges all six sources. On the
  machine it was built on that is **106 programs out of about 300 registry keys**, the difference
  being Windows' own rules for what counts as a program rather than a patch record, a system
  component or a shared runtime.

  Removing runs the program's own uninstaller — silently where one genuinely can be, including
  MSIs, where `MsiExec.exe /I{GUID}` is rewritten to `/X{GUID} /qn /norestart` — and the row says
  **before you press it** whether it will be unattended or ask questions. Then it sweeps up the
  folders and registry keys the uninstaller left behind.

  **Nothing is deleted.** Folders go to the Recycle Bin via `FOF_ALLOWUNDO`; registry keys are
  exported to a `.reg` file in `%ProgramData%\Nostos\removed\` before they are removed. And a
  failure is never turned into a delete: if the Recycle Bin will not take something, or a key
  cannot be backed up, the item is left where it is and the reason appears on the row. Doing the
  irreversible thing at the moment the reversible one turned out to be unavailable is precisely
  backwards.

  That is what makes an automatic sweep defensible, because **finding leftovers is name matching
  and name matching is guesswork.** Knowing for certain what an installer created means having
  watched it install, which needs a filesystem filter driver — a kernel driver, which this
  program will not ship. So the guessing is made as narrow as it can be: a candidate must match
  the name in full and never as a substring (substring matching is how uninstalling "Microsoft
  Teams" proposes deleting `%LOCALAPPDATA%\Microsoft`), only a fixed set of parent directories is
  looked in, the registry side is a fixed list rather than a search, and a drive root, Windows,
  Program Files, the user profile and `WindowsApps` are refused whatever the match says.

  See [docs/uninstaller.md](docs/uninstaller.md).

  **Every row has an icon.** The registry's nominated icon first; then, for a packaged app, the
  artwork out of the package itself -- a Store app's launcher is a zero-length reparse point that
  the shell cannot read, so its logo has to be found in `Assets\` (or `Images\`, which is not the
  convention but is what MSI Center uses), usually in a sibling package ending `_split.scale-100`
  rather than the one the registry names. Failing both, the largest executable in the install
  directory. That covers **89 of the 105 programs** on this machine; the remaining sixteen are
  redistributables and SDKs that genuinely ship no artwork anywhere, and they get a monogram tile
  coloured by a hash of the name — stable between launches, so the same program is the same
  colour every time. A list where a sixth of the rows are blank squares reads as broken.

  The page also names where the registry backups go, because "keys are exported before they are
  removed" is a promise worth much less if the reader cannot see where the file went.
- **A Details button on every row, answering "why is this even listed?"** A row exists because
  something registered it — almost always a key under `…\CurrentVersion\Uninstall` — and that key
  is not the program. An uninstaller that deletes a program's files without deleting its own key
  leaves an entry Windows still believes in, so a game removed months ago is still in the list.
  Until now the only thing the window could say was that it was installed, which to somebody who
  knows it is not reads as the list being wrong rather than the registry being stale.

  The panel names the exact registry key, in full and selectable, then checks every path the entry
  records and says which of three cases it is: everything still there, nothing still there, or in
  between. The middle one is a real shape rather than an evasion — a game whose files were deleted
  while its launcher stayed is neither installed nor a leftover. Under that is what the leftover
  scan finds on disk under the same name, with sizes, and with anything that looks like your own
  files marked.

  It is read on demand rather than for every row, because the scan measures folder sizes and doing
  that a hundred times to fill a panel nobody opened would make the tab slow to earn nothing.
- **Removing an entry that outlived its program.** The panel's button takes the registration and
  everything listed, without running an uninstaller — which is the point of it, because `Remove`
  runs the program's own uninstaller and then refuses to sweep while the program is still
  registered. That is right when the program is real and a dead end when it is not: there is no
  uninstaller left, or there is one that shrugs because its program is already gone, and the entry
  is still there either way.

  Deliberately a second button rather than a mode of the first. The two make different claims
  about the machine — "uninstall this" and "the program is gone, take its entry away as well" —
  and one button that quietly did whichever seemed to apply would be guessing on the reader's
  behalf about the one thing they are deciding.

  Same reversible machinery as everything else: the key is exported to a `.reg` file in
  `%ProgramData%\Nostos\removed\` before it goes, folders go to the Recycle Bin, and a failure is
  never turned into a delete. Only a registry entry can be forgotten — a Store package or a scoop
  directory is the program rather than a note about it, so the button is not offered there — and
  the registration is removed last, so a sweep that fails half way leaves the row in the list with
  the rest of it still findable rather than hiding it.
- **Each thing an entry owns can be removed on its own.** The Details panel lists every folder and
  registry key found under a program's name, the entry itself, and an icon file it records when
  that sits outside those folders — each with its own Remove button, alongside the one button that
  takes all of them. "The registry entry is stale but I want to keep the folder" and "clear the
  lot" are both ordinary things to want, and offering only the second sends somebody to regedit
  for the first.

  The list of what a program owns is **rebuilt from the machine before any single removal, and the
  path has to be on it.** That check is the security boundary rather than a nicety: removal takes
  an app id *and* a path, and without the pairing it would be a way to recycle any folder on the
  machine, or export and delete any registry key, by naming a program alongside it. Windows' own
  directories are refused by that check like anything else the program does not own, before the
  protected-path list is consulted at all.

  The uninstaller is never offered. Deleting it strands a program that is still installed with no
  supported way to remove it, which is worse than the position somebody opened the panel to get
  out of.

  An icon earns its place in the list because the leftover scan cannot reach it: a stale entry
  often points at a file under the publisher's folder rather than the program's — Riot keeps a
  game's icon in `ProgramData\Riot Games\Metadata\<game>` — and the scan matches on the program's
  name and refuses substrings, for good reasons. It is a real leftover that nothing else here
  would offer to clear up.
- **A Cleanup tab: space taken by files that rebuild themselves.** Shader caches, scratch
  directories, launcher caches, crash reports — 22 named locations, each with its size, how many
  files, and a sentence saying what you lose. On the machine it was built on that is **9.3 GB**,
  of which 4.5 GB is a single folder: the NVIDIA driver's compiled shaders, which is also the
  first thing to clear when a game stutters after a driver update.

  **A fixed list of named places, never a search for folders that look like caches.** That
  distinction is the whole safety argument and it is not theoretical: on this machine
  `Steam\steamapps\downloading` held **172 GB** — a paused game update, in a folder whose name and
  contents look exactly like disposable staging. A cleaner matching on shape rather than on
  knowledge would have taken it. What is refused is written down with its reason, next to what is
  offered: the Steam download and workshop folders, Downloads, Prefetch, `Windows.old`, the page
  and hibernation files, browser profiles, and the Recycle Bin.

  These are **deleted rather than recycled**, which is the one place in this program that does
  that, and the list itself earns it: everything on it regenerates, so there is nothing to undo —
  and recycling would free no space until the bin was emptied, which is where the undo for
  uninstalls lives. The rule to hold when adding a location is the same one: if it does not
  regenerate, it does not belong on the list.

  The sweep never removes the folder itself, never follows a junction or symbolic link out of it
  (the directory walk is hand-written because the framework's recursive enumeration follows
  them), and skips files in use rather than failing. Scratch directories take only files
  untouched for a day — 5,540 of 17,678 held back here, which the row says next to the size, so a
  total that looks too small has its explanation beside it. Shader caches have no age rule on
  purpose: making somebody wait a day defeats the reason they came.

  Four machine-wide locations are measured either way and skipped with a reason when the app is
  not elevated, because "you would get this back elevated" is worth knowing and "blocked" alone
  invites elevating for nothing. See [docs/cleanup.md](docs/cleanup.md).
- **Where administrator rights are needed, it is said before the click, not after.** One rule,
  applied across every tab that has the problem: *when the program knows in advance that
  something cannot be done, the control stays and says what it needs.*

  Five places were getting this wrong, in three different ways. The Cleanup tab **removed** the
  Clean button from its four machine-wide rows, which teaches somebody that this program cannot
  clean Windows' own temp folder rather than that their copy is not elevated. The Tweaks and
  Profiles tabs offered Apply, sent it, and printed whatever the engine said when it refused — a
  sentence written for somebody at a command line, after a round trip. The Startup tab let the
  switch move, watched HKLM refuse the write, re-read the entry and put the switch back, which
  is the visual language of a broken control rather than of a permission somebody could go and
  get. The Apps tab's Details panel offered Remove on HKLM keys and Forget on machine-wide
  entries, and answered with reg.exe saying access was denied without saying whose.

  Now every one of them carries a **Needs administrator** badge from the moment it is drawn, the
  button stays where it was, and pressing it says what would make it work. The advice differs by
  what would actually fix it: a cleanup target is emptied by this process, so the answer is to
  start it as administrator; a machine-wide tweak or startup entry has a standing way to be
  changed without an elevated window, which is the background service — one approval, once.

  The Cleanup page also states what an elevated launch would additionally reach ("2 GB more is
  within reach…"), which is what those four measurements were always for, and a profile card
  says how many of its tweaks would be skipped before Apply is pressed rather than by marching
  through forty rows marking most of them skipped afterwards.

  Nostos still never asks for those rights itself: the manifest stays `asInvoker`, and every
  sentence here ends by saying what to start differently.

### Changed
- **Opening the Apps tab no longer freezes the window.** It blocked for **487ms** the first time,
  on the shipped build, and nothing was being read — the list had been in memory since startup.
  The tab was being built: an `ItemsControl` in a `ScrollViewer` creates every row it is given,
  and a hundred programs with an icon, five pieces of text, two chips, two buttons and a folded
  details panel each is several thousand controls laid out in one pass on the UI thread. A
  `ListBox` builds the dozen rows that fit and the rest as they scroll into view. **487ms became
  25ms**, and nothing about the list looks different.

  The other lists were already virtualised or small enough not to matter, and the measurements
  are in [docs/architecture.md](docs/architecture.md): Cleanup 3ms, Profiles 7ms, History 1ms.
- **The window starts faster and stops freezing while it loads.** Measured on the shipped
  ahead-of-time build with a trace through every phase and a heartbeat on the UI thread: the
  window still appears in 256ms, every tab is filled in **529ms instead of 661ms**, a first
  launch reaches the catalog in **603ms instead of 1,687ms**, and the UI thread is now blocked
  for **0ms after the window appears, where it used to be 323ms**.

  Five causes, all the same mistake in different places: work that did not need the UI thread was
  on it, and work that did not need doing was being done.

  Bootstrapping — creating folders, hashing and rewriting profile files, asking the SCM about the
  service — ran inline on the UI thread of a window already on screen. Icons did too: a hundred
  programs and fifteen startup entries, each a registry search and a shell call, 173ms of a
  window that could not repaint. The catalog, startup and apps reads waited for each other
  despite sharing nothing. The first-run checklist's 1.2-second pause was awaited *before*
  loading began rather than alongside it. And fifty of the eighty-nine tweaks were read a second
  time under selections identical to the ones the bulk read had already used — fifty extra calls
  per refresh, which on an installed copy is fifty round trips over the pipe, four times a
  minute, forever.

  A background refresh that finds nothing new is also silent now. Assigning a row's status
  invalidated every property on it, so the five-second re-read relaid out all eighty-nine rows to
  redraw exactly what was there.

  The one deliberate wait left is the Cleanup tab, which measures when you open it because it
  walks seventeen thousand files, and says so while it does. See
  [docs/architecture.md](docs/architecture.md).
- **The window's text is bigger, from one named scale.** Everything was set from six different
  literal sizes between 10 and 15px, and the smallest of them carried the most: the annotation
  under each row saying what a tweak writes, where a cache lives, what clearing it costs. Those
  lines are most of what this program is, and they were set at the smallest size available.

  Every size now comes from nine tokens in `Palette.axaml` — `FontLabel` through `FontDisplay`
  — and no view sets a number, which is the rule the corner radii already followed. The whole
  scale moved up two to four points rather than only the bottom of it, so the hierarchy that was
  there is unchanged: what was quieter than the body text still is. Fixed dimensions holding text
  moved with it — column widths, both dialogs, the window's own minimum size — because text
  that grows inside a box that does not is text that gets clipped rather than read.

  Two things this turned up on the way: the Cleanup tab's heading was set in a class that does
  not exist, so it had been drawing at body size while every other tab's heading was a title; and
  the History tab's opening paragraph ran the full width of the window, which at this size is a
  line about two hundred characters long.
- **The buttons on the Apps tab look like buttons.** Details and Remove were borderless, which is
  right for something that only needs to be findable once and wrong for the two controls a row
  exists to offer. Details is now an ordinary surface button and every removal is tinted, so the
  destructive one of a pair is the one that looks it. Per-item buttons use a new compact size,
  because a full-size button at the end of a line of text outweighs the line it acts on.

### Fixed
- **A per-item removal said nothing at all, whether it worked or not.** Pressing Remove beside a
  registry entry deleted the key, exported its backup, and left the window exactly as it was: the
  code reloaded the tab, and the tab's reload rebuilds tweaks, history and profiles but not the
  installed list. A success and a failure were indistinguishable on screen — both were nothing —
  so the only way to find out which had happened was to open regedit.

  Both outcomes are now stated on the row. A failure marks it failed and shows the reason; a
  removed registration marks the row the way a finished uninstall is marked, says the entry is
  gone and that the `.reg` backup can put it back, and closes the panel rather than going on
  listing an entry that is no longer there.

- **A row could not show a status at all, which is how the above stayed invisible.** The status
  line's visibility binds to `HasStatus`, and the state setter announced that the text had changed
  without announcing that there was now a text to show. Every finished removal computed the right
  sentence, passed its tests, and never drew it. The properties a state change affects are now a
  list the setter iterates and a test asserts against, because `PropertyChanged` is marshalled to
  a UI thread that no test runs — so an omission there is invisible to the entire suite, which is
  exactly what happened.
- **An uninstall command with no path in it was reported as a missing uninstaller.**
  `MsiExec.exe /X{…}` is the commonest uninstall string on any machine and Windows resolves it
  against the search path; checking the bare name resolves it against the working directory
  instead. Fifteen rows on the machine this was built on were affected, each one wrong, in the
  exact place somebody has come to find out whether a row can be trusted.
- **Every Store app lost its icon unless Nostos was run elevated, and elevation is exactly what
  this program does not do.** Listing `C:\Program Files\WindowsApps` requires administrator
  rights — a plain user gets `UnauthorizedAccessException` from the directory enumeration —
  while *reading inside* a package whose name you already know is allowed, and always was. The
  search looked for a package's split resource siblings by listing that root, so for anybody not
  elevated the very first call threw and all 28 packaged apps on this machine went to monograms
  at once.

  Sibling package names are no longer discovered; they are **constructed**. A split resource
  package shares its parent's identity, version and publisher hash and differs only in the scale
  — `Microsoft.WindowsCalculator_11.2502.2.0_x64__8wekyb3d8bbwe` implies
  `Microsoft.WindowsCalculator_11.2502.2.0_neutral_split.scale-100_8wekyb3d8bbwe` — and Windows
  defines the set of scales, so every candidate can be written down and checked with
  `Directory.Exists`, which a plain user is allowed to do. Nothing in the icon search enumerates
  that folder any more. Measured unelevated afterwards: 26 of 28 packaged apps find their logo,
  the same as when elevated.

  The package's recorded location is used as the starting point rather than
  `%ProgramFiles%\WindowsApps` being assumed, so an app installed to another drive — which lives
  under that drive's own `WindowsApps` — is now found too.

  Worth recording how this survived: it was reviewed, covered by a test that reads the real
  machine, and photographed several times, and every one of those ran elevated. A test that
  builds its own package folders in an ordinary directory now covers it, because it exercises
  the search with no privileged path involved at all.
- **Programs that plainly have a logo were being drawn without one, Epic Games Launcher among
  them.** The search knew three places to look and an MSI keeps its artwork in a fourth: as a
  property inside the package rather than as a file, cached at install time under
  `%WINDIR%\Installer\{ProductCode}\` and filed in the installer's own product database. Epic has
  no `DisplayIcon`, no `InstallLocation` and nothing on disk carrying its name, so every step of
  the old search came back empty on a program whose icon Programs and Features shows without
  difficulty.

  Two details made it more than a registry read. Products are filed under a **packed** product
  code -- the GUID with its first three fields reversed and the rest byte-swapped -- and getting
  that transform wrong yields a key that simply does not exist, which is indistinguishable from a
  product having no icon; the tests hold it against three real pairs read off a machine. And the
  cached files are often extensionless (`ProductICO`, `ARPIco`), which the shell identifies files
  by, so they are read straight out of the file's resources instead.

  Also added: a program's own uninstaller as a source, since it is usually branded even when
  nothing else is, excluding any that lives inside the Windows directory -- msiexec, rundll32,
  cmd and powershell in one rule. And a nominated icon is now checked against the disk, because
  the shell answers for a file that is not there: it hands back the blank-page icon for the
  extension, which is worse than nothing since it is not null and so ended the search on a row
  that had a real icon waiting two steps further down.

  That takes the programs found with their own artwork from **89 of 105 to 94** on the machine it
  was built on. The remaining eleven -- seven Visual C++ runtimes, a couple of SDK components --
  ship no artwork anywhere: no nominated icon, no cached one, no install directory, no Start menu
  shortcut. They are now drawn the way Programs and Features draws them, with the icon Windows
  uses for an installer package, so no row is blank.
- **The sweep no longer trusts an uninstaller's exit code, and this was very nearly shipped.** A
  Steam game's uninstaller is `steam.exe steam://uninstall/960090`: that process hands the request
  to the Steam already running and exits in about a second with code zero, long before Steam has
  drawn the dialog asking whether you meant it. A sweep conditioned on the exit code would have
  moved 118 GB of Elden Ring to the Recycle Bin while the user was still deciding — and would have
  done the same after they pressed Cancel. Epic and several other launchers hand off the same way.

  The exit code is not the evidence; the machine is. After the uninstaller returns, Nostos
  re-enumerates, and if the program is still registered it is still installed: nothing is swept,
  and the row says to finish in the launcher and come back.

  Found by dry-running the scan across all 106 programs on a real machine and reading what it
  proposed, which is also how two other bad proposals turned up: Store packages putting their own
  directory under `Program Files\WindowsApps` forward as a leftover, and the Store package called
  "DirectX" putting forward `HKLM\SOFTWARE\Microsoft\DirectX`, which is Windows' own key. The scan
  now runs only over Win32 installers, and `WindowsApps` is refused outright.
- One program's missing artwork can no longer empty the whole list. Decoding a package's PNG
  needs Avalonia's render interface, which does not exist in a headless test run; the exception
  escaped the row's constructor, emptied the collection and left the tab blank. Found by test.
  An icon is decoration and the list is the feature, so a failure there now falls through to the
  monogram.
- The installed-programs list is filled on the UI thread. Filling a bound `ObservableCollection`
  from the thread pool crashed the window with "Collection was modified; enumeration operation may
  not execute", thrown from inside Avalonia's own item generator as it walked the list being
  rebuilt underneath it. The three-hundred-key registry walk still happens off-thread; only the
  collection and the icon bitmaps come back.

## 0.2.0 - 2026-08-31

### Added
- **Applying a profile is now something you can watch.** Expert is 76 tweaks and the better part
  of a minute, and until now the only thing on screen during it was a spinner over a list that
  did not move. "Is it stuck, or is it working?" was the only question a reader had, and nothing
  answered it.

  The card opens itself, and each row shows where the batch has got to: a spinner and an accent
  bar on the tweak being worked on right now, a green tick behind it for each one that landed, a
  muted dash for each one deliberately skipped, and a red cross for anything that failed. A bar
  and a `12 of 42` under the description. The activity panel names the current tweak too, so the
  answer is still on screen from another tab.

  **The reports come from inside the loop that does the work** — `TweakEngine.ApplyManyAsync`
  takes an optional callback and fires it either side of each tweak. The easy version of this
  feature is an animation timed to look plausible while the real batch runs somewhere else, and
  it is the wrong one: it would keep ticking cheerfully past the tweak that failed, and there is
  no test that could tell you it was lying. This one stops where the batch stops. A batch refused
  as a whole — a conflict, or the safety gate — reports nothing at all rather than a phantom run.

  A backend that cannot report real progress passes no callback, and the bar stays
  **indeterminate** rather than sitting at "0 of 42" and then jumping. That is the service's
  `profile-apply`, which is one request and one response over the control pipe; adding progress
  frames to a privilege boundary is not something a progress bar justifies. In practice the
  window does not take that path — it wraps the service in `SplitBackend`, which already applied
  a profile a tweak at a time and now says so.
- **A Windows Update tab**, gathering everything this program can do about Windows Update onto
  one page: the driver swap, the restart, the toast, the feature update, and both of the
  downloaders that compete with a game for the line.

  **Drawn from the `windows-update` tag, not from a category**, because those tweaks deliberately
  sit in three different categories — stopping a driver swap is *Crashes & Freezes*, a restart
  toast is *Interruptions*, and a background download competing for the link is *Ping*. A
  category is a claim about what a tweak does for the player, and "which part of Windows it
  writes to" is not one of those. So the categories stay exactly as they are and this is a second
  way in, for a reader who arrived thinking "Windows Update" rather than thinking "ping".

  The rows are **the same view models the Tweaks tab holds**, not copies. Two view models over
  one tweak is how a page ends up reading ON while the page beside it reads OFF, with nothing on
  screen to say which is right.

  Clicking a row switches it, the way a Startup row switches a program — apply if it is off,
  **revert if it is on**, so the undo restores the value that was captured rather than whatever
  this program believes the default to be. Risk and evidence chips stay on every row: these are
  real tweaks with real trade-offs, and a friendlier-looking page is the wrong place to stop
  saying so. Pinning a Windows version is Moderate and looks it.

  The page also says outright what it will not do, at the bottom, where it answers a question the
  list raises rather than leading with an apology. See
  [docs/windows-update.md](docs/windows-update.md).
- **Five Windows Update tweaks**, built around the three specific ways Windows Update ruins an
  evening rather than around "turn it off".

  - **`update.no-driver-updates`** — the one worth having. Windows Update ships graphics drivers
    alongside security patches through the same client, on the same schedule, behind the same
    *Check for updates* button, and will overwrite the driver you installed on purpose. A machine
    that was stable last week and stutters this week very often has a driver nobody chose. Four
    values, because Windows offers drivers down two separate paths and closing one leaves the
    other open: `ExcludeWUDriversInQualityUpdate` for the monthly quality update, and the three
    `DriverSearching` values behind the old *Device installation settings* dialog. Security
    updates keep arriving.
  - **`update.active-hours`** — the window Windows Update will not restart or interrupt you in.
    The default is `SmartActiveHoursState = 1`, meaning **Windows guesses it** from when the
    machine sees activity, which is fine on a laptop and poor on a desktop that is signed in
    continuously — the signal it reads there is "always on". Three windows to choose from;
    Windows caps the span at 18 hours, which is why none of them is the whole day.
  - **`update.no-restart-notifications`** — the toast saying a restart is needed takes focus when
    it appears, and a focus change in a fullscreen game is a dropped frame at best and a
    minimised window at worst. Both the policy value and the Settings one, because they are read
    by different parts of the update experience.
  - **`update.pin-windows-version`** — `TargetReleaseVersion`, the current documented way to hold
    a machine on one release, rather than the `DeferFeatureUpdatesPeriodInDays` other tools still
    write. Quality and security updates keep arriving; only the feature update — several
    gigabytes, a forty-minute reboot, and some settings back at their defaults — is held. Rated
    **Moderate** for a reason that is easy to miss: a pin has an expiry date and Windows will not
    remind you, so when the pinned version leaves servicing the machine simply stops receiving
    security updates, with no error.
  - **`update.store-auto-download-off`** — the Microsoft Store is a second updater with its own
    schedule that none of the Windows Update settings touch. Filed under **Ping** for the same
    reason as Delivery Optimization: a background transfer competing for the link is a latency
    problem, not a bandwidth one.

  **What is deliberately not here** is the "disable Windows Update" preset every other tool
  ships. Beyond it being a bad idea, it does not fit: it deletes `C:\Windows\SoftwareDistribution`,
  which cannot be captured, so it cannot be reverted, so it cannot be an `ITweak` at all — and the
  service half would be repaired by WaaSMedic on its own schedule, leaving Verify reporting drift
  forever. A tweak that permanently reports itself broken is worse than an absent one.

- **An edition gate on declarative tweaks**, `"proOnly": true`, checked in
  `RegistryTweak.CheckApplicabilityAsync` beside `minBuild` and `desktopOnly`, and backed by a
  new `SystemInfo.Edition` reading `EditionID`.

  Windows Update for Business policies are the one class of registry value where **every
  observable sign says the change worked and nothing happened**: the key is writable on Home, the
  value stays where it was put, a read-back returns it, and Verify reports no drift — the update
  client on Home simply never reads it. That is worse than a failure, because a failure says so
  and this would sit in the list with a tick beside it. Two of the five above are gated.

  `EditionID`, not `ProductName`, which still reads "Windows 10 Pro" on a Windows 11 machine. And
  the question is asked as "is this Home" rather than "is this Pro or better": the edition list is
  long and grows, and an edition this program has never heard of should be offered the tweak,
  because offering one that turns out to do nothing is a smaller failure than hiding one that
  would have worked. A catalog test requires every `proOnly` tweak's docs page to say so.
- **A Startup tab.** Everything Windows launches when you sign in — machine-wide Run keys, the
  32-bit Run key, your own Run key, and both Startup folders — in one list with the program's own
  icon on each row and a switch on the right. On a gaming PC this is usually the largest
  background load on the machine and the one nothing in the tweak catalog could touch.

  **It is deliberately not a tweak list.** No catalog entry per program, no risk rating, no
  evidence claim, because the catalog cannot know what is installed on your machine: there is no
  defensible risk rating for "Razer Synapse", which is essential if you own a Razer mouse and
  pure background load if you do not. That is a fact about the reader, not about Windows. The tab
  shows what is there and gets out of the way, and it promises no frames.

  **Nothing is deleted.** Switching a row off writes the same `StartupApproved` record Task
  Manager's Startup tab writes, so the entry itself is never touched, the two tools agree about
  the state, and uninstalling Nostos leaves nothing behind that Windows cannot manage on its own.
  The alternative — delete the Run value and remember it in our own journal — is why uninstalling
  some debloaters leaves a machine that has forgotten how to start its own audio driver.

  The approval byte turned out to be a **bitmask, not an enum**: `0x02` and `0x06` both mean
  enabled, `0x03` and `0x07` both mean disabled, and bit 0 is the flag. Reading it as a value
  works on most machines and then reports one entry backwards on a machine with an `0x06` or an
  `0x07` in it — this one has an `0x07`, Windows Security's tray icon. Writing preserves the
  other bits rather than clobbering them with a constant; verified on that entry, `07` → enable →
  `06` → disable → `07`.

  Icons are read from the executable and decoded from the icon's own bitmaps, with two cases a
  naive version gets wrong: icons older than 32-bit colour carry no alpha and come back fully
  transparent, so the shape is taken from the AND mask; and Store apps launch through a
  zero-length reparse point the shell cannot open, so the generic icon for the type is used.
  Teams is exactly that case.

  Writes follow the same split as the tweaks, per entry: per-user ones are done by the app in
  your own session, because `HKCU` inside the LocalSystem service is SYSTEM's hive and the write
  would succeed while changing nothing; machine-wide ones go to the service. The pipe carries
  "switch the entry called `machine-run:Portmaster`", never a registry path and a payload — the
  service resolves the id against the live machine and refuses anything that is not already a
  startup entry. A refused write leaves the row where it was and puts the reason above the list,
  so **the row never shows a state the machine does not have**.

  Also on the command line: `nos startup`, `nos startup enable|disable <id>`.

  **Every switch is recorded in the History tab**, from all three places that can make one — the
  window, the service and the CLI — so the tab stays a complete account of what this program did
  and tells one story wherever the switch was flicked. The line is a committed change with no
  preceding intent, which is load-bearing: the outstanding set `nos revert --all` works from is
  built out of intents carrying a snapshot, so these are visible in the history and are never
  something revert goes looking for. Undoing an unrelated tweak months later must not silently
  turn Razer Synapse back on. There is no snapshot because there is nothing to snapshot — the
  prior state is one bit, it is visible in Task Manager, and undoing it is one click in the tab
  that did it. See [docs/startup.md](docs/startup.md).

  Scheduled tasks are **not** in this list yet. Plenty of software starts from a logon-triggered
  task, so the list is not complete without them, but they switch through a different mechanism
  and shipping half of it would have meant a tab where some rows behave differently from others.
- **New tweak: "Always start a game at a higher priority"** (`process.persistent-priority`),
  Performance, Moderate. The permanent counterpart to "Prioritise a running game process", and
  the answer to that one's real complaint: it raises a process that is already running, so it has
  to be done again after every launch, and nobody opens a window before each session to do it.

  This records a priority under Image File Execution Options instead, which the Windows loader
  reads at process creation. Point it at a game once — from the same **Target process** picker,
  or with `--set exe=cs2.exe` for one that is not running — and every future launch starts that
  way, across reboots, with nothing running to arrange it.

  |  | `game-tuning` | `persistent-priority` |
  |---|---|---|
  | Applies to | one running process | every launch of an image name |
  | Survives restart | no | yes |
  | Takes effect | immediately | next time the game starts |
  | Also sets EcoQoS | yes | no — there is no loader equivalent |
  | Risk | Safe | Moderate |

  Moderate rather than Safe, and the docs page says why before you apply it: the setting is
  machine-wide and matched on the bare file name, it survives reboots, and it takes effect before
  anything — including Nostos — can intervene. Only **Above normal** and **High** are offered
  even though the registry accepts Idle and Below normal; a permanent setting that makes a game
  slower on every launch is not something the catalog should be able to do by accident. Above
  normal is the recommendation here, where High is the recommendation for the session-only one.

  **Revert removes every game, not just the selected one**, and that follows from something worth
  writing down. The journal keeps one snapshot per tweak id — the oldest, so applying twice still
  reverts to the machine as it was. This tweak is applied once per game. A snapshot holding only
  the game being set would leave the second game with no record and no way back: permanent,
  machine-wide, and invisible. So each capture records *every* permanent priority on the machine,
  which makes the oldest-snapshot rule do exactly the right thing.

  Verified end to end on a machine that already had an entry under that key from something else:
  two games set at different priorities, then one revert, and both were removed — keys and all,
  not just their values — while the pre-existing entry was left at the value it had.

  `TakesTargetProcess` is now a field on a tweak rather than something derived from its scope.
  This one writes HKLM and outlives every process it affects, so it is machine-scoped, and it
  still has to be told which executable; deciding from the scope alone left it with a question it
  could not ask.

- **German**, selectable in Settings. English stays the default and is never chosen for you:
  a German Windows install is not on its own a statement that somebody wants a half-English
  window, and picking German is one click that is then remembered.
  - The whole interface is translated, including all 85 tweak titles and summaries, every
    choice and option a tweak offers, what every category promises, the journal, the startup
    checklist and the removal flow.
  - Switching applies **immediately**. Every string is either a binding through the table or a
    computed property, so the open window rewrites itself; nothing reloads and no selection,
    scroll position or in-flight operation is lost.
  - Untranslated strings fall back to English rather than to a blank or a crash, so adding a
    tweak never has to wait for a translator. Two tests enforce the parts that must not rot:
    the two string tables must hold the same keys with the same `{0}` placeholders, and every
    id in the German catalog must be a tweak that still exists.
  - **Terms this audience says in English stay in English.** The first pass rendered every
    term literally, so the performance category promised to raise the "Bildrate" and two
    services were accused of costing "Bilder pro Sekunde". Both are correct German and neither
    is what anybody who plays games says: they say FPS. A short glossary is now enforced by
    tests over both the string table and the tweak catalog. It holds only terms German borrows
    whole — "Dienst", "Treiber" and "Arbeitsspeicher" are what German actually uses and are
    left alone.
  - **The category names are not translated at all**, in any language. They are the program's
    vocabulary rather than prose: the same names label a docs page, a tweak's `category` field
    and a CLI filter, and several of them — Performance, Input Lag, Ping, Xbox — are what a
    German player says anyway. Translating some and not the rest produced a sidebar that
    read as though nobody had finished it. The promise under each name is prose, and is
    translated.
  - Text that reaches the window **already written in English** is translated where it is
    displayed, not where it is produced: a profile's description, which is a JSON file the user
    can edit, and a tweak's "not applicable" reason, which is produced by a service running as
    SYSTEM that has no user and therefore no language. Both now travel with a key beside the
    English, and the window picks. A profile somebody wrote themselves still shows their own
    words, and a reason from a build that predates the key still shows the English it sent.
  - A tweak's **raw state** (`HwSchMode = 2 [Mode: Aggressive]`) is deliberately not translated
    in either language. It is registry value names and the numbers behind them, and somebody
    comparing this window against regedit or a forum post has to see the same characters in all
    three places.
  - The **CLI stays English**. It has no settings panel to choose in, and its output is parsed
    by scripts.
- **A settings panel**, behind the gear in the window's top right. It holds two things.
- **Update preferences**: whether to check GitHub at all, and whether to check every launch,
  once a day or once a week, plus a **Check now** button. A check that fails is reported here
  and nowhere else, because here somebody asked for it.
- **Remove Nostos from this PC**: reverts every applied tweak through the ordinary revert path,
  stops and deletes the background service, and deletes `%ProgramData%\Nostos` and the per-user
  renderer cache. Afterwards the only thing left is the folder the app runs from, which the
  panel names. System Restore points are deliberately left alone. See
  [docs/uninstall.md](docs/uninstall.md).

### Changed
- **The profile list is reconciled in place instead of cleared and rebuilt.** It used to copy a
  set of open card names across a rebuild to keep them open, which worked while a card was inert
  data. It does not work now that a card can be mid-apply: the live loop reloads every few
  seconds and would drop the object the progress reports were being delivered to, leaving the
  rest of the run invisible.
- A profile card's rows are stable objects rather than records rebuilt on every read, so a row
  can carry live state. The price is that the card has to be told when the language changes,
  which the window already does.
- **`stability.driver-search-off` gained the policy half of the setting it already owned**:
  `DontSearchWindowsUpdate`, `DontPromptForWindowsUpdate` and `DriverUpdateWizardWuSearchEnabled`
  under `Policies\Microsoft\Windows\DriverSearching`. `SearchOrderConfig`, which it already wrote,
  is a *preference*, and a preference is something Windows feels free to re-derive — a feature
  update or a repair install can put it back. The policy is not.

  It wrote two values in 0.1.0 and writes five now. Capture happens at apply time, so a journal
  entry from the old build covers only the two it knew about; revert it and apply it again to put
  the new three under the program's control.
- **The control-pipe protocol is now v5.** `TweakSummary` carries the tweak's tags, so the window
  can group by something other than category without a second source of truth. A version mismatch
  is already a hard error that tells you to reinstall the service, so there is no half-upgraded
  state this has to stay compatible with.
- **The Startup tab now looks like something you can change.** The whole row is the button: it
  lifts under the pointer and a click switches the entry. Before this, the only thing on screen
  saying any of it could be changed was a small toggle's hover state at the far right, which you
  had to already be on top of to see — a list of fifteen programs where nothing looks clickable
  reads as a report rather than a control panel.

  The word "On"/"Off" is now a switch with a knob, and both states are always in the tree with
  one of them transparent, so a flipped row crossfades instead of blinking. A control that
  visibly moves when clicked is one people believe they can click.

  The hover colour is deliberately **not** `SurfaceHover`. That brush is a translucent white
  meant to sit on top of whatever is underneath; on a row that paints its own `SurfaceFlat`
  background it does not lighten the card, it replaces it — so the hovered row became a pale band
  composited over the panel behind, and the dimmed text of a switched-off program washed out on
  top of it. It is now an opaque colour two steps lighter than the card.
- **A profile card now says what it would actually change on your machine.** Opening one used to
  list forty-two tweaks with no indication of which were already set, so the card asked you to
  agree to work that was mostly already done — on this machine, Basic reads *20 of 21 already
  applied*.

  Each row now carries a green tick for what the machine already matches and an empty circle for
  what applying would change, and the applied rows are dimmed so the eye lands on the remainder.
  The count is on the card header too, because it changes what the Apply button is offering:
  "42 tweaks" and "36 of 42 already applied" are very different propositions.
- **The rows are grouped by category instead of repeating it.** The flat list carried the
  category as a word on every one of forty-two rows — a heading pretending to be a column, saying
  the same thing four times running and giving the list no shape. Each category is now a heading
  with its own `applied/total` count, in sidebar order, so a card reads down in the same sequence
  as the catalog it is drawn from.
- The History tab's introduction no longer claims that everything in it can be undone with
  **Revert everything**. That was true when only tweaks appeared there, and startup switches are
  deliberately not part of it.
- **"Unused Features" and "Background Services" are now two categories that mean different
  things.** Unused Features held twenty tweaks and every one of them was a service, so the name
  described nothing: there was no distinction being drawn, only a label implying one.

  The line between them is **who is qualified to decide**. Unused Features names things you
  recognise — Bluetooth, printing, Xbox and Game Pass, Fax, smart cards, sensors — and can settle
  in a second from facts about your own life that this program has no access to. Background
  Services names things almost nobody has heard of — AllJoyn Router, Distributed Link Tracking,
  the TCP/IP NetBIOS Helper — where you have no basis for an opinion and the tweak has to supply
  one. That is a real difference in how the two lists get read, which is what a category is for;
  "is it a service?" would not have been, because they are all services.

  Both promises are held to their half of that bargain by tests: every Background Services page
  has to explain what the service actually does, and every Unused Features page has to name what
  stops working.
- **Xbox is no longer its own category**; the four Xbox services moved into Unused Features. They
  were separated on the argument that they are one decision rather than four — true, and equally
  true of printing, of Bluetooth and of Fax, none of which got a sidebar entry of their own. "Do
  you use Game Pass?" is the same shape of question as "do you have a printer?", so it belongs in
  the same list. Ten categories, still.
- `services.xbox-networking` had a docs section headed "Why it is filed under Ping" left over
  from an earlier home, arguing from the service's job rather than from the decision. It put a
  row in front of people looking to lower their ping that was never going to lower anyone's ping.
- **Fixed: "Prioritise a running game process" could not be used from the window at all.** It is
  the one tweak that acts on a single running process, and the window had no way to name one, so
  it reported itself not applicable with "no target process specified" — true, and useless — for
  every launch since it was added. The only way to use it was `nos apply process.game-tuning
  --pid`.

  There is now a **Target process** picker above the Apply button, listing running programs that
  have a window. Choosing one re-reads the tweak against it, which is what moves the row out of
  "not applicable": for this tweak, applicability is not a fact about the machine, it is whether
  the question has been answered yet.

  The plumbing for a target already ran the whole way from the IPC contract to the engine; only
  the window never filled it in. The target travels beside the options rather than inside them —
  options are a tweak's own choices and go into the journal as the record of what was asked for,
  and a process id is neither a choice nor worth keeping once the process has exited.

  The picker reads only the identity Windows already publishes: name, pid, window title. Nothing
  opens a process for memory access, which is the line this program does not cross.
- **Process-scoped tweaks are now carried out by the app rather than the service.** The service
  runs as SYSTEM in session 0, so a pid it is handed comes from a session it cannot see and
  needs a privilege to reach into. The app is already in the right session with the right token,
  and the tweak declares that it needs no elevation.
- **The three profiles are now a ladder: Basic, Intermediate, Expert.** They used to be
  `conservative`, `competitive` and `streaming` — three different goals, which meant picking one
  required already knowing which of three things you were. Depth is the question people actually
  have, so that is what they answer now. Each rung contains the one below it, enforced by a test:
  "everything in Basic, plus…" has to stay true, or moving up a rung would silently revert
  something.

  | | Tweaks | What it adds |
  |---|---|---|
  | **Basic** | 21 | Rated Safe, no reboot, switches nothing off that you might be using. |
  | **Intermediate** | 42 | Changes whose cost is a reboot or a named trade-off, plus telemetry and advertising identifiers. |
  | **Expert** | 76 | Turning Windows features off: unused services, Xbox, indexing, SysMain, NTFS bookkeeping. |

  **No profile applies anything rated Risky or Experimental**, and a test enforces that too. A
  profile is one click, and one click must not be able to leave a machine unbootable; those two
  tweaks stay in the catalog with their own warnings.
- **A profile card opens to show exactly what it would apply** — every tweak, in the profile's
  own order, with its category and risk. "Apply 42 tweaks" was a lot to agree to on the strength
  of one sentence, and the honest answer to "what does this change?" was to go and read a JSON
  file. The whole header strip is the hit target, not just the arrow.
- **Shipped profiles are kept up to date without ever overwriting your edits.** Each file is
  decided on its own against a record of what the app last wrote there
  (`%ProgramData%\Nostos\shipped-profiles.json`): a file that still matches what we wrote is one
  nobody has touched, so an improved profile replaces it; a file that does not match is yours
  and is left alone. The old rule — seed only into an entirely empty folder — meant a profile
  improved in a later release reached nobody who had ever run the app, including the release
  that renamed all three of them. Deciding by *name* instead would have been worse: it would
  silently revert an edited `basic.json` on the next update.

  The three superseded files are renamed to `.superseded` rather than deleted. They are ours
  and nobody is expected to miss them, but a profile is a file you are invited to edit, and
  deleting an edited copy to tidy up after a rename is a larger act than this program is
  entitled to.
- **Profiles have an explicit order**, so the list reads Basic, Intermediate, Expert rather than
  Basic, Expert, Intermediate. A ladder sorted by file name says the opposite of what the names
  mean.
- **Fixed: "Remove the Widgets board from the taskbar" offered a button that could never work.**
  Applying it failed, the engine rolled the change back, and the window said "Did not work".

  The cause is not a bug in this program and not a permissions problem, which is what made it
  worth chasing. When Widgets is turned off machine-wide by Group Policy
  (`HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests = 0`), Windows refuses every
  write to `TaskbarDa`, the per-user value the tweak sets — from an elevated process, and from
  SYSTEM. The key's ACL grants full control, the key opens for writing, and creating any *other*
  value in it succeeds; what refuses is a kernel registry callback rejecting that one value name
  because the policy owns the setting. Nothing this program could read said so in advance, and
  .NET reports it as `UnauthorizedAccessException` — "Attempted to perform an unauthorized
  operation" — which sends you to look at elevation, which never helps.

  A tweak can now declare the policy that can take its setting away, and reports itself **not
  applicable** while that policy is in force, naming it so you can go and lift it if you want
  the tweak. Checked on every read rather than cached, because a policy can arrive or be lifted
  between two launches.

  A write that is refused anyway — by a policy nobody has catalogued yet — now says what
  actually happened instead of the framework's message. Auditing the other 62 registry values
  the catalog writes on the machine this was found on: every one of them accepts a write, so
  this was the only affected tweak there.
- **Five new categories, replacing "Background & Cleanup".** That one bucket held 38 of the 84
  tweaks — nearly half the catalog under a single heading, thirty of them the same sentence with
  a different service name in it. A bucket that large is not a category, it is the absence of
  one: nothing in it could be found except by reading all of it, and its promise had to stay
  vague enough to cover the Fax service and NTFS timestamps at once.

  | Id | Shown as | Tweaks |
  |---|---|---|
  | `telemetry` | Telemetry & Privacy | 6 |
  | `startup` | Startup & Boot | 4 |
  | `xbox` | Xbox Services | 4 |
  | `unused` | Unused Features | 20 |
  | `storage` | Disk & Filesystem | 2 |

  Xbox gets its own because it is one decision, not four: somebody who does not use Game Pass
  wants all of them off and should not have to find them one at a time among twenty unrelated
  services, and somebody who does use it wants a single thing to skip.

  `background` is no longer a category, so `nos list --category background` now fails with the
  list of real ones rather than returning half the catalog.
- **Two tweaks were filed in the wrong place.** "Stay on the bluescreen instead of rebooting
  instantly" is a crash tweak and now sits under Crashes & Freezes. "Remove the menu open delay"
  is under Input Lag & Aim, which is where its own docs page had said it belonged all along —
  the page and the catalog had disagreed since it was moved into the bucket, and nothing checked
  that, because the check only reads the header line the move rewrites.
- **Every band of the tweak list now runs safest-first.** The old order inside a band was
  alphabetical by tweak id, which put rows in a sequence no reader could see a reason for.

  Inside a category the bands are the risk levels — Safe, Moderate, Risky, Experimental — each
  saying what that level means. A category is already a promise, so once you are in one the only
  question left is what a change costs if it goes wrong.

  Across the whole catalog the list is banded twice, because it is ordered by half of the
  catalog and then by category, and one heading could only ever name one of them. It named the
  half, which made the ordering underneath look broken: a single "Gaming" heading sat over four
  categories in a row, so the risk column ran safe-to-moderate four separate times with nothing
  on screen marking where one category ended and the next began. There is now a Gaming/Windows
  heading and a category heading under it, which is the same shape the sidebar draws.
- **Three badges could never be read in German**, and two string-table entries translated values
  that do not exist. `risk.high` and `scope.session` name nothing — the enum members are `Risky`
  and `Process` — so a risky tweak's badge fell back to the English "risky", an experimental
  one to "experimental" and a per-process one to "process". Nothing failed: the fallback for a
  missing key is the enum member's name lower-cased, which looks exactly like a word somebody
  chose. There is now a test that holds every enum the window puts a word on against both
  tables, in both directions.
- **The update banner's second line now says only what version you are on.** It used to carry
  the first line of the release notes as well, which sounds useful and is not: release notes
  are markdown written for a changelog, so what landed in the banner was whichever sentence
  happened to come first, truncated mid-clause. It now reads "You have version 0.1.0. Update
  now." — the one fact the banner needs to add is the one the headline does not already give.
- **The window opens in about a quarter of a second.** The folder build now compiles the app
  ahead of time, which is where nearly all of the saving is: almost every millisecond of the old
  startup was spent JIT compiling Avalonia on the way to the first frame. Measured on the same
  machine, same build, time from process start to a window on screen:

  | | before | after |
  |---|---|---|
  | folder build (`dist`), warm | 1,212 ms | **264 ms** |
  | folder build, first run | 3,223 ms | **743 ms** |
  | one-file build (`portable`), warm | 250 ms | 266 ms (unchanged) |

  ReadyToRun was measured too, at 640 ms — better than IL and half as good as ahead of time, so
  it is not what shipped. Only the window is compiled this way: the service starts once at boot
  and the CLI is measured against how fast somebody types, and each one would cost minutes of
  build time. The app folder also gets smaller, since the .NET runtime it used to carry is now
  inside the executable.

  What is left is not ours to remove: of the remaining ~265 ms, ~195 ms is Avalonia bringing up
  Win32, Skia and ANGLE before any Nostos code runs, and ~30 ms is Windows loading the image.
  Wiring those three backends by hand instead of letting Avalonia detect them was tried and
  measured at 2 ms, which is noise, and it crashed the first attempt outright by leaving out
  text shaping.
- **The one-file build can be published again.** The German support added a markup extension
  that returned a reflection binding, and a reflection binding cannot be compiled ahead of time:
  `dotnet publish -p:PublishAot=true` failed with IL2026 and IL3050, so the primary download
  could not be built at all. Localized text now binds to an observable instead, which reaches
  the same place with no reflection and no dynamic code. A compiled binding to an indexer was
  tried first and is the tidier-looking answer: it renders and never updates, because Avalonia's
  compiled bindings do not re-read an indexer on the `Item[]` notification.
- **The window draws its own title bar.** `WindowDecorations="BorderOnly"` plus
  `ExtendClientAreaToDecorationsHint` removes Windows' bar and its caption buttons and covers
  the frame's top edge, and the app puts back what that bar was doing: the mark and the name on
  the left, minimise / maximise / close on the right, and the whole strip as a drag handle with
  double click to maximise. Both settings are needed — BorderOnly alone leaves the top of the
  frame showing as a grey strip, and extending the client area alone leaves Avalonia drawing a
  second set of caption buttons over ours.
  The frame itself is deliberately kept rather than removed with `WindowDecorations="None"`,
  which was measured rather than assumed: "None" clears `WS_THICKFRAME`, and a window without
  that bit cannot be resized by dragging any of its edges.
  The caption buttons sit above both overlays, so a modal can never be a state the program
  cannot be closed from.
- **The window has been redesigned.** The look is now a design system rather than colours
  written inline: `src/Nostos.App/Styles/` holds the palette, the control templates and the
  shared classes, and every view is built out of those. Concretely:
  - Vibrant on a quiet ground. Surfaces sit within a few points of each other so the only
    saturated things on screen are the ones carrying meaning — whether a tweak is on, how risky
    it is, how well evidenced it is. Risk and evidence are filled chips now rather than coloured
    words, and each colour has a matching tint to fill its chip with.
  - Rounded throughout, by kind rather than by eye: 16 for panels, 12 for cards and rows, 10 for
    controls, full for pills.
  - Buttons, tabs, checkboxes, radio buttons and list rows are the app's own templates. Avalonia
    12 reworked the Fluent theme and dropped the WinUI style brush keys, and overriding a key
    that no longer exists fails silently — a template that names a property wrongly fails the
    build instead.
  - Motion: everything that changes colour transitions rather than snapping, buttons give way
    slightly when pressed, the tab strip is a segmented control whose selected pill is the one
    gradient in the window, and each screen fades up as it arrives. The two loops that were
    already there, the row spinner and the live pulse, are unchanged.
  - The three tabs, the two overlays and both empty states were each checked on screen.
- The History tab and the tweak detail panel say what they are waiting for instead of showing an
  empty rectangle. A blank page and a page that failed to load look identical, and for a tool
  whose whole claim is that it keeps a record, "the record is empty" and "the record is missing"
  are not the same thing to leave a reader guessing between.
- Profile descriptions are capped at 900px rather than running the full width of a maximised
  window, and **Apply** is no longer an accent button: three profiles are three equal choices,
  and a screen with three primary buttons has none.
- The catalog is now data end to end. The 35 service tweaks and 4 network-adapter tweaks moved
  out of `CatalogFactory.cs` — which was 534 lines, almost all of it a list wearing a
  constructor — into `services.json` and `adapters.json` beside `registry.json`. The factory is
  46 lines, adding a service tweak no longer touches C#, and the catalog it produces is
  byte-for-byte the one it produced before.
- The catalog files are formatted for people: a registry value is one line rather than seven,
  which took `registry.json` from 1,663 lines to 984 with no change to its content.
- Dropped the Windows SDK projection from every project. Nothing in the codebase uses a WinRT
  API, and targeting `net10.0-windows10.0.19041.0` was shipping a 23.7 MB
  `Microsoft.Windows.SDK.NET.dll` in every folder build for it. The folder build is 128.7 MB
  → 104.6 MB.
- Deleted `BackendFactory`, which nothing had called since the bootstrapper took over choosing
  a backend.

- `Nostos.Ipc` folded into `Nostos.Core`. Six projects instead of seven: everything that
  referenced the IPC assembly already referenced Core, and 380 lines do not earn a csproj. The
  `Nostos.Ipc` namespace is unchanged, because it names a versioned wire protocol rather than a
  folder.
- `MainWindow.axaml` split into the window shell plus `TweaksView`, `HistoryView` and
  `SettingsView`: 802 lines to 326, with each screen in a file named after it. The value
  converters moved to application scope, which is what lets a view resolve them at all once it
  lives in its own file.
- Catalog filtering moved out of `MainWindowViewModel` into `CatalogFilter`, where it is a pure
  function of (tweaks, category, search) and testable without a window.
- The one-file build is compiled for size: **30.05 MB to 25.59 MB**, a 15% smaller download.
  `UseSystemResourceKeys` and stack-trace removal were deliberately left off — they would save
  more, and they would turn "Access to the path is denied" into "Arg_UnauthorizedAccess" in a
  program that shows those messages to users.

### Fixed
- **`update.no-driver-updates` was a duplicate of `stability.driver-search-off` and has been
  removed.** Both wrote `ExcludeWUDriversInQualityUpdate`, and two tweaks writing one registry
  value is a trap the journal cannot dig anyone out of: apply both, revert one, and the other's
  value is silently undone while its row still reads ON.

  It was missed because the search for existing coverage was done on the id prefix `update.`
  rather than on the registry values, and the tweak that already owned driver updates is called
  something else. A test now asserts that no two tweaks on the Windows Update page write the same
  value, which is the check that would have caught it.
- The bundled profiles are written atomically. An interrupted first run could leave a zero-byte
  `conservative.json` behind, and since the profiles are only written when the folder is empty,
  that file then survived forever: every launch afterwards reported "could not start: The input
  does not contain any JSON tokens" over a catalog that was perfectly fine. Found by killing the
  app mid-startup while timing launches.
- Unit tests no longer make a live HTTP request to GitHub. Every test that loaded the main
  window used to run the launch-time update check against the real API.
- An update check refused by GitHub's rate limit now says so, and says when it clears, instead
  of reporting a bare `403`. The limit is 60 requests an hour **per IP address**, so everyone
  behind the same connection shares it — a state users behind carrier NAT reach without doing
  anything wrong, and could not previously tell apart from a broken network.

## 0.1.0 - 2026-08-26

First public release.

### Added
- **84 tweaks**, in two groups and six categories.
- **`nos bench`** — network latency measurement reporting median, p95, p99 and jitter rather
  than an average, with `nos bench compare` bootstrapping a confidence interval before it will
  call a difference real. See [docs/benchmark.md](docs/benchmark.md).
- **`nos update`** — checks GitHub for a newer release and installs it, verifying an ECDSA
  signature over the release checksums before anything is written.
- Four network adapter latency tweaks: interrupt moderation, receive coalescing,
  energy-efficient ethernet and flow control.
- A "Not applicable on this PC" section in both the app and `nos status`, for tweaks that need
  hardware or a service this machine does not have.

### Changed
- **The auto-revert watchdog is gone.** Nothing undoes a change on its own any more; what you
  apply stays applied until you revert it. A System Restore point is still taken before a risky
  or reboot-requiring batch. Control-pipe protocol went to v3.
- A tweak that cannot be applied on this machine always displays as OFF, whatever its own read
  reported.
- The `Folklore` evidence tier was removed. Every tweak is listed regardless of how well
  evidenced it is; the pages say plainly which ones probably do nothing.
