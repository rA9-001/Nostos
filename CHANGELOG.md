# Changelog

Notable changes per release. Versions follow [semantic versioning](https://semver.org): while
the major version is `0`, a minor bump may change behaviour.

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
