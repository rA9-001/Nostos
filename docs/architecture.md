# Architecture

## The shape

Three processes, one privilege boundary.

```
Nostos.App (user, unelevated)  ─┐
                                ├─ named pipe, ACL'd to the installing user's SID
Nostos.Cli (user)              ─┘
                                   │
                                   ▼
Nostos.Service (LocalSystem, auto-start)
   ├── TweakEngine       apply / revert / verify / journal
   ├── Journal           append-only, %ProgramData%
   └── Reconciler        re-apply what Windows has reset
```

The split exists for one reason: **applying a tweak must not raise a UAC prompt every time.**
Elevate once at install, and after that the UI is an ordinary unprivileged client.

## Nothing runs while you play

There is no process watcher. Nothing enumerates running processes, nothing matches executable
names against a list of games, and nothing reacts to a game starting or exiting.

This was removed deliberately, and it is worth being explicit about why, because the feature is
attractive and most tools in this category have it. A background service that wakes on a timer
to enumerate processes, notices a game, and then reaches into that game to change its
scheduling is behaviourally hard to distinguish from the first stage of something much less
friendly. Anti-cheat systems are in the business of noticing that pattern. The upside was
saving the user one click before launching; the downside was a plausible route to a false
positive on an account someone cares about. That is not a good trade.

So the model is: **optimise, then play.** Apply what you want, close the app if you like, and
the machine keeps the settings. The service that stays resident does three things, none of
which involve looking at what you are running:

- answers the control pipe when a client connects to it,
- checks a marker file for a risky change nobody confirmed after a reboot,
- re-applies tweaks that Windows itself has reset, every 30 minutes.

Profiles still exist and are still the fast path, but a profile is a named set of tweaks you
apply on purpose, not a mode that switches itself on. Per-game automation may come back later
as something explicitly opt-in and off by default. It is not worth having on by default.

The service is the trust boundary, so the pipe is ACL'd by SID: SYSTEM and Administrators with
FullControl, plus the SIDs recorded at install time with read/write. A world-writable control
pipe into a LocalSystem process that edits HKLM is a local privilege escalation, and it is the
hole most tools in this category actually have.

One non-obvious detail, found the hard way: the account the daemon runs under also needs
FullControl, because `CreateNewInstance` is only included in that right. Without it only the
*first* pipe instance can be created and every additional listener fails with access-denied —
silently capping the service at one concurrent client.

The CLI talks to the engine **directly** rather than through the service. That keeps the engine
usable and auditable standalone, and it is how integration tests drive real tweaks without
installing anything.

## Projects

| Project | Target | Why it exists |
| --- | --- | --- |
| `Core` | `net10.0` | Engine, journal, profiles, safety. **No Windows dependency, no NuGet packages.** The logic that decides what happens to your machine must be readable and testable without a Windows SDK. |
| `Win32` | `net10.0-windows` | Registry, power schemes, process control, P/Invoke. Hand-written interop, deliberately small. |
| `Tweaks` | `net10.0-windows` | The catalog. Declarative JSON entries plus native tweaks. |
| `Ipc` | `net10.0` | Control-pipe contract and client. Portable, so the wire format is testable anywhere. |
| `Service` | `net10.0-windows` | LocalSystem host, ACL'd pipe server, reconciler. No process watcher. |
| `Cli` | `net10.0-windows` | `nos`. Thin shell over the engine, or over the pipe with `--service`. |
| `App` | `net10.0-windows` | Avalonia desktop app. The only project with a third-party dependency. |

## The ordering guarantee

`TweakEngine.ApplyOneAsync` is the heart of the project:

1. **Applicability** — right OS build, right hardware, target process alive?
2. **Elevation** — machine-scope tweaks are *skipped with a reason*, never half-attempted.
3. **Read** — already applied? Then do nothing and say so.
4. **Capture** — record the prior value.
5. **Journal the intent** — `ApplyIntent` with the snapshot goes to disk **before** the mutation.
6. **Mutate.**
7. **On failure** — revert from the snapshot we already hold. If the revert also fails, report
   that plainly, including the journal path.
8. **Verify** — read back. A change that did not stick is reported as `Unverified`, not success.
9. **Journal the result.**

Step 5 before step 6 is the whole design. It is why a bluescreen mid-apply costs you nothing.

## Why the journal is JSON Lines

A corrupt or partially written line can be skipped without losing the other 400 entries, which
a single top-level JSON array could not survive. It is also readable with `type journal.jsonl`
when someone is troubleshooting a machine that will not boot the UI.

`GetOutstandingAsync` replays the whole log rather than maintaining a mutable "current state"
file. A torn write costs at most the last entry instead of the ability to restore the machine.

Reverting keeps the **oldest** snapshot for a tweak: applying twice must still revert to the
value the machine had before this program ever touched it.

`ApplyFailed` and `RevertFailed` leave a tweak **outstanding**. A failed apply may have changed
something before it threw, and a failed revert certainly has not finished undoing it.

## Safety machinery

**System Restore point** before any batch containing a `Risky` or reboot-requiring machine-scope
tweak. If one cannot be created, the batch is refused unless `--no-restore-point` is passed.

**Nothing auto-reverts.** There used to be a second net here: a marker written after a risky
batch, and a timer that undid the listed tweaks unless somebody confirmed the machine still
worked. It is gone. A change this program makes stays made until a person undoes it, in the app
or with `nos revert`.

The trade is deliberate and worth stating. What the watchdog bought was the headless case - a
machine that comes back with no picture cannot be told that everything is fine, so the change
came out by itself. What it cost was a program that reached into a working machine and removed
settings the user had chosen, on a schedule they did not set, because they had not clicked a
banner. The second thing happened far more often than the first.

The restore point above is what remains for the headless case, and it is reachable from the
Windows recovery environment, which is where you are if it happens.

**Reconciliation.** `TweakEngine.ReconcileAsync` re-applies persistent tweaks that the journal
says should be on but the machine says are off. Windows Update silently resets several of these
keys; without this, settings quietly rot and users blame the game.

## User-scoped tweaks

Some tweaks live in `HKCU`. A service running as `LocalSystem` that writes to `HKCU` writes to
*SYSTEM's own hive*, which achieves nothing.

The catalog therefore marks these `"scope": "User"`, and a CI test enforces that a User-scoped
registry tweak only ever touches `HKCU`. When the service applies one it must first impersonate
the console user (`WTSQueryUserToken` on the active session, then `ImpersonateLoggedOnUser`).
The CLI, running as the user already, has no such problem.

## Anti-cheat compatibility

This is a hard design constraint, not a preference.

**Never:** kernel drivers, `PROCESS_VM_READ`/`PROCESS_VM_WRITE`, DLL injection, API hooking,
patching game memory, or anything that makes the program indistinguishable from a cheat.

**Only:** documented Win32 APIs operating on processes from the outside —
`SetPriorityClass`, `SetProcessInformation(ProcessPowerThrottling)`, `SetProcessAffinityMask`,
and registry/power/service configuration that the OS itself reads.

Everything worth doing is reachable this way. The interop surface in
[`NativeMethods.cs`](../src/Nostos.Win32/Interop/NativeMethods.cs) is deliberately
small enough to read in one sitting and confirm that.

## The app's two backends

The window never knows whether it is talking to the service or to the engine in-process:

```
MainWindowViewModel ──> IOptimizerBackend ──┬── ServiceBackend  (control pipe, no elevation ever)
                                            └── LocalBackend    (engine in-process)
```

`BackendFactory` prefers the service and falls back on `ServiceUnavailableException`. The
fallback is not an error path — the app is fully usable without the service, just without
automatic profiles and without machine-scope changes from an unelevated session — so the reason
is carried through to the UI and shown in a banner. A control that cannot work should say why,
not sit there inert.

The transport DTOs from `Ipc` are reused as the app's own model rather than mapped into a third
set of types.

The app's manifest requests `asInvoker` deliberately. It must never elevate: the product's
promise is one UAC prompt at service install and none afterwards.

## Service jobs

Two concurrent jobs, cancelled together on stop:

| Job | Interval | Purpose |
| --- | --- | --- |
| Control pipe | — | 4 concurrent connections, newline-delimited JSON, size-capped requests |
| Reconciler | 30 min | Re-applies tweaks Windows has reset |

Both are idle unless something happens. Between a pipe connection and a reconcile pass the
process does nothing at all.

## Known limitations of the service

**User-scoped tweaks never go over IPC.** SYSTEM has its own user hive, so an `HKCU` tweak sent
to the service would read and write SYSTEM's settings rather than yours. The daemon refuses
writes with an explanation; reads were the sharper edge, because they succeeded and returned the
wrong answer — a tweak the user had applied showed as off because SYSTEM's hive did not have it.

`SplitBackend` routes around this: the app runs as the signed-in user, so it does user-scoped
work in-process and sends only machine and process scope over the pipe. The CLI does the same by
default, and `--service` on a user-scoped tweak is still refused rather than misapplied.

The real fix is for the service to impersonate the console session (`WTSQueryUserToken`, then
`ImpersonateLoggedOnUser` around the write). Until that exists, the work happens where the right
hive already is.

**One outstanding change per tweak.** The journal keys outstanding changes by tweak id, so
applying a tweak twice under different choices without reverting in between leaves a single
snapshot — the older one. Declarative tweaks work around this by capturing every value any
option could write, so a revert still restores all of them; a native tweak with options has to
handle it itself. The general fix is an instance key in the journal.

## Testing

`Core` has no Windows dependency, so the engine's ordering guarantees are tested exhaustively
against an in-memory `FakeTweak` — including the cases that are hard to produce on real
hardware: apply throws mid-mutation, revert throws too, a torn journal line, concurrent appends,
drift after Windows Update.

The IPC contract is exercised through round-trip tests: a payload that fails to round-trip is a
request the privileged service would misinterpret. That includes the per-option descriptions,
which are the entire reason a choice exists and would otherwise be an easy thing to drop on the
wire without anyone noticing.

`Tweaks.Tests` enforces catalog rules in CI: unique ids, a docs page per tweak, the docs page
states the evidence rating, the docs page names the category it claims, every category has at
least one tweak in it, machine scope implies elevation, session-only implies no reboot,
reboot-requiring implies at least Moderate risk, User scope implies HKCU only, and — for tweaks
that offer choices — at least two options, a default that exists, at most one recommendation,
and a real description on every option rather than a bare label.

## The window never blocks, and never shows stale state

Two properties the app has to hold, and one non-obvious bug that broke both.

**Every tweak's leaf operations are synchronous inside.** A registry write, an SCM call, a
`powercfg` invocation — each wrapped in an already-completed `Task`. Awaiting one of those does
not yield: the continuation runs inline on the calling thread. So `await backend.ApplyAsync(...)`
from a command handler ran the whole operation on the UI thread, and the window was frozen for
its duration.

The busy flag and the spinner were already there and already correct. They just could not
paint, because the thread that would have painted them was busy doing the work. The code read as
properly asynchronous at every level; only running it showed otherwise. It never appeared over
the service, where a named pipe is genuinely asynchronous — it was specific to portable mode and
to user-scoped tweaks, the two paths that run in-process.

`LocalBackend.OffUiThread` is the fix, applied at the one boundary where the app stops talking
to something remote and starts doing the work itself. Everything after the await returns to the
UI thread, because the view model's own awaits do not `ConfigureAwait(false)`.

**`ObservableObject.Raise` marshals to the UI thread** when it is not already on it. With engine
work on the pool, a view model property can be set from a pool thread, and an Avalonia binding
updated off the UI thread is an exception at best. Doing the check centrally means no caller has
to remember, which is the only version of that rule that stays true.

**The catalog re-reads itself every five seconds.** Not a Refresh button the user has to think
about. These values are changed by things other than this app: Windows Update resets several,
the service's reconciler re-applies drifted ones, and Settings can change any user-scoped one
while the window is open. A display that is only correct straight after you clicked something
lies for the rest of the session.

Three things make that affordable:

- The read runs on the thread pool, so a tick costs the UI nothing.
- `ApplyFilter` reconciles the bound collection in place instead of `Clear()`-and-refill.
  Emptying a collection bound to a ListBox drops its scroll position and selection, so a
  five-second refill would make the list jump under the reader's cursor. A tick that finds
  nothing changed mutates nothing and repaints nothing.
- The background tick skips the journal and the profile list, which only change when something
  acts on them. Over the pipe that is two round trips per tick instead of four.

The loop never runs while a user operation is in flight — re-reading mid-apply could report a
half-applied tweak as final — and its failures are swallowed, because the service restarting
underneath is the common case and the next tick recovers.

**Progress lives in a permanent panel, and fast work never uses it.**

This took two goes. The first version was a banner that appeared while work was running, which
solved the visibility problem — a 3px bar in the bottom-left corner is the place people look
least — and created two new ones. It shifted every panel below it down and back, and because
most operations now finish in tens of milliseconds it flashed. A progress indicator that comes
and goes within two frames reads as a rendering fault, not as progress.

The fix is two independent changes:

- **The panel is always there, at a fixed height.** Nothing about it appears or disappears; only
  its contents change, and they cross-fade via `Opacity` rather than `IsVisible`, because
  collapsing an element resizes its parent. When idle the space is not wasted: it holds the last
  result, how many changes are outstanding, and the freshness clock. That let the separate
  bottom status strip go away entirely — one place for status instead of two.
- **`ActivityTracker` decides whether progress is worth announcing at all.** Work that finishes
  inside `ShowAfter` (180ms) never shows an indicator; from the user's side it simply happened,
  which is the truth. Work that crosses that line holds the indicator for at least
  `MinimumVisible` (650ms), because without that the flicker just moves to operations slightly
  slower than the threshold. Consecutive operations hand the indicator over rather than letting
  it drop between them.

The distinction between `IsRunning` and `IsVisible` matters and is deliberate. The background
refresh loop asks `IsRunning`, so it never overlaps a user operation; the view asks `IsVisible`,
so it never flickers. Conflating them would either race an apply or blink the window. Delays are
injected, so the state machine is tested exactly rather than raced.

**The activity panel says how old what you are looking at is** (`LastUpdatedText`), next to a pulse
that only shows while the loop is alive. The claim is that nothing displayed is outdated; that
pair is where a user gets to check it rather than take our word for it.

## Results are translated, in one vocabulary

The engine answers with `mmcss.system-responsiveness: applied — SystemResponsiveness = 10
[Background CPU reservation: Balanced - 10%]`. That is a dotted identifier, an enum name and a
registry assignment: exactly the record worth keeping, and exactly the wrong thing to show
somebody who has just pressed a button.

`ChangeSummary` splits it into a headline saying what happened and a second line saying what it
means or what to do next. Three rules do the work:

- **The verb goes in front of the title, never around it.** Catalog titles are already
  imperative — "Turn off mouse acceleration", "Stop Store apps running in the background" — so
  the obvious phrasing produces "Turned on Turn off mouse acceleration". `Applied — <title>`
  reads correctly for every entry in the catalog and needs no per-tweak phrasing.
- **Every outcome ends by saying what state the machine is now in.** A rolled-back apply is the
  case that matters most: something failed, and the one thing worth saying is that the PC is as
  it was. An apply needing a reboot says so instead of implying an effect, because a user who
  goes looking for a difference that cannot be there yet concludes the tool did nothing and
  applies five more things to compensate.
- **A batch is counted, not listed**, but anything that went wrong is named. "8 changed, 2
  failed" hides which two and why.

The History tab uses the same words for the same events, so an entry in the log and the message
that appeared when it happened are recognisably the same thing.

## The history tab is a translation, not a dump

The journal on disk is a machine record: an action enum, a tweak id, a free-form origin tag and
a registry fragment. That is exactly the right shape for the thing `nos revert --all` reads, and
exactly the wrong shape for somebody asking "what has this program done to my computer".
`ApplyCommitted / mmcss.system-responsiveness / gui` is not an answer to that question.

`JournalEntryViewModel` translates, and three decisions do most of the work:

- **Tweaks are named, not identified.** The id is what the journal stores; the title is what the
  reader gets. An entry whose tweak has since left the catalog falls back to the id, because a
  record of a change you cannot name is worse than an ugly name.
- **Intents are folded away.** Every change writes an "about to do this" row before touching
  anything, which is what makes a crash mid-apply recoverable. It is bookkeeping, not an event,
  so a matched pair collapses to the row that says what happened. An intent with no outcome is
  the interesting case: it stays, says "started but never finished", and raises a banner,
  because it is the one journal state that asks the reader to act.
- **Origins are explained.** `reconcile` in particular has to explain itself — a change
  reappearing on its own is alarming unless you are told Windows had reset it. `watchdog` is
  still translated too, for the lines older machines already have in their journals from when
  the auto-revert timer existed.

The exact values stay, in monospace, underneath. A tool whose whole claim is that it can prove
what it changed does not get to hide the proof; it just does not get to lead with it either.

## Measuring: why ping is in and FPS is not

`nos bench` measures network latency. See [benchmark.md](benchmark.md) for what it reports and
what it refuses to claim.

Framerate is the obvious companion and it is deliberately absent, because the normal way to get
it is closed to this project by its own rules.

**The usual way is an overlay.** RTSS, MSI Afterburner, Steam's counter and every in-game FPS
overlay work by injecting a DLL into the game and hooking its `Present` call. That is precisely
what this program promises never to do -- no injection, no hooking, nothing in another process's
address space -- and the promise is not decoration. It is the reason a player can run this
alongside EAC, BattlEye or Vanguard without thinking about it.

**There is a legitimate way, and it is not cheap.** Windows emits ETW events from the graphics
stack -- the `Microsoft-Windows-DXGI` and `Microsoft-Windows-DxgKrnl` providers -- describing
every swap-chain present: when the application submitted the frame, when the GPU finished it, and
when it reached the display. Intel's PresentMon is built on exactly this, which is why it can
report frametimes for any API without touching the game. Consuming ETW is read-only, entirely
out-of-process, and needs privilege the LocalSystem service already has.

The cost is that it is a subsystem, not a feature:

- **No library.** The usual .NET answer is Microsoft's `TraceEvent` package, and taking it would
  break the rule that Avalonia is the only third-party dependency and that nothing privileged has
  any. Doing it in-house means hand-rolled interop over `StartTrace`, `EnableTraceEx2`,
  `OpenTrace` and `ProcessTrace`, plus a manifest-based event parser.
- **The event stream is not the answer.** Turning present events into a frametime is genuinely
  intricate -- deferred and discarded presents, flip versus blit, multi-plane overlay, windowed
  versus exclusive fullscreen. PresentMon's consumer is thousands of lines and it is the
  reference implementation because getting it right is hard.
- **It is not universally allowed.** Some anti-cheat implementations block ETW trace collection
  for their titles. A framerate feature that silently returns nothing for the games people most
  want to measure has to say so.

Three ways forward, none of them free:

1. **Hand-rolled ETW consumer.** Fits the dependency rules exactly and keeps everything
   auditable. The largest piece of work in the project so far.
2. **Shell out to Intel's PresentMon.** The same pattern as `Checkpoint-Computer` and `netsh` --
   drive a documented tool and parse its CSV. Fast to build, and it means shipping or fetching a
   third-party binary, which is a distribution and trust decision rather than a technical one.
3. **Measure the desktop instead.** `IDXGISwapChain::GetFrameStatistics` on a window this program
   owns gives real present timings with no ETW and no third party -- but it measures *this*
   program's frames, not the game's. Honest, cheap, and answers a question nobody asked.

Option 2 is the pragmatic choice and option 1 is the one consistent with everything else here.
Neither has been started.

## Categories are claims, not folders

A tweak's `category` names the thing a person would notice if it worked. There are six, they
live in `TweakCategories`, the set is closed, and each one sits in one of two **groups**:

| Group | Id | Shown as | What filing a tweak here claims |
| --- | --- | --- | --- |
| Gaming | `performance` | Performance | Raises the framerate *and* evens out frametimes, by freeing CPU, GPU or memory Windows was spending elsewhere |
| Gaming | `input-lag` | Input Lag & Aim | Shortens or steadies the path from mouse and keyboard to screen |
| Gaming | `ping` | Ping | Cuts round-trip network latency, or stops Windows adding delay of its own |
| Gaming | `stability` | Crashes & Freezes | Fixes a specific fault that shows up while playing, rather than making a working machine faster |
| Windows | `interruptions` | Interruptions | Stops things appearing over what you are doing, stealing focus, or restarting the machine |
| Windows | `background` | Background & Cleanup | Stops Windows running services and features you do not use. **Does not promise frames.** |

**The group is the more important half of the split.** `fps` and `stutter` used to be separate
and nobody was choosing between them: a tweak that frees CPU raises the average *and* fills in
the dips, so filing it was a coin-flip the reader then had to guess at. They are one bucket now.

What was genuinely missing was the other axis. Turning off the Fax service and extending the
GPU timeout are both reasonable things to do and have nothing to do with each other, and a
single flat list said otherwise — twenty-one service tweaks sat under "Stutter & 1% Lows"
claiming a frametime benefit that most of their own docs pages went on to disclaim. Anything
whose mechanism does not reach the game is now filed under **Windows**, where the bucket's own
promise says out loud that it is not about frames.

The one deliberate exception is `services.delivery-optimization`, which is a service tweak
filed under Ping: saturating the uplink is a direct, demonstrable cause of a ping spike. If a
service tweak wants to sit in the Gaming half, that is the bar.

The obvious alternative was to file by subsystem — `cpu`, `gpu`, `network`, `shell` — which is
what the catalog did first. It is easier to assign and it is nearly useless: nobody has ever
sat down at a machine wanting more shell. Worse, it hides the interesting differences. Under
subsystem filing, `shell.notifications-off` and `shell.background-apps-off` are neighbours,
when one is stopping a popup and the other is freeing CPU; `gpu.tdr-delay` and `graphics.hags`
are neighbours, when one changes nothing about speed and the other is a latency change.

The rule that makes the field worth having: **the category is a promise, so it has to be
falsifiable.** A tweak whose only honest benefit is fewer popups belongs in `interruptions`
even though it is obviously tempting to file it under `fps`, because a user who clicks FPS and
applies six things is owed six things that plausibly move their framerate. This is the same
argument as the `Evidence` field, applied to a different lie.

Consequences in the code:

- **`TweakMetadata.Category` validates on the way in.** An unrecognised value throws at
  construction. Left unchecked, a typo surfaces as a plausible-looking extra bucket in the
  sidebar with one tweak in it, which nobody would question.
- **Ordering is declared, not alphabetical.** `TweakCategory.Order` drives the sidebar, `nos
  list` and `nos status`, so the listing opens on FPS rather than on "Crashes & Freezes".
- **Categories carry search synonyms.** `TweakCategory.Keywords` holds the words people
  actually type — "hitching", "framerate", "rubber-banding", "flicker" — none of which appear
  in any tweak's title. Without them the search box answers a reasonable question with an empty
  list, which reads as "this tool does not do that".
- **CI checks the claim is defended.** Every docs page has to name the category it is filed
  under, and every category has to contain at least one tweak. An empty category is a promise
  the tool does not keep.

Nothing about the category affects what a tweak *does*. It decides where it appears and what
was claimed for it, which is exactly why it is worth being strict about.

## The service optimizer

Turning Windows services off is the single most destructive thing tools in this category do,
and the damage is rarely traced back: somebody disables forty services from a forum list, and
three months later their firewall is off, their controller does not work, and they have no idea
why. The design here is shaped entirely around not being that.

**One tweak per service.** Not one "optimize services" button over a list of forty. Each
service is its own catalog entry, which means each one needs its own docs page justifying it
(CI enforces that), carries its own evidence rating, and reverts on its own. The friction is
the point: it is hard to add forty services when each one has to be argued for in writing.

**The bar for inclusion is honesty, not benefit.** An earlier version of this section said a
tweak that could not claim a category honestly did not get added, and used the Print Spooler as
the example of one that was left out. That was the wrong test, for a simple reason: leaving a
tweak out does not stop anybody making the change. It only stops them making it somewhere that
writes down what the value was before. The bar now is that the docs page says what actually
happens — including "this will do nothing for your framerate", which several of these pages say
in as many words — and the evidence rating carries the claim. Most service entries are rated
unproven, and their pages say so in as many words.

**Manual is the default, not Disabled.** `SERVICE_DEMAND_START` means the service does not start
at boot but can still be started by anything that asks. If the reasoning on the docs page turns
out to be wrong for a particular machine, that machine quietly keeps working. `SERVICE_DISABLED`
means it cannot start at all and whatever needed it fails with an error naming neither the
service nor this tool. Disabled is offered — sometimes it is genuinely what you want — but it
is never the recommended option, and a test asserts that for every service tweak in the catalog.

**A protected list, enforced at the bottom.** `WindowsServices.Protected` names services this
tool will not rewrite under any circumstances, each with a sentence saying what breaks. It is
deliberately narrower than it used to be, and the line it draws is between a *preference* and a
*fault*.

A preference is a service whose absence is a decision somebody can weigh up in advance: no Xbox
controller, no Bluetooth, no printer, no Game Pass. Those are in the catalog now, with pages
that say plainly which one breaks the controller. `XboxGipSvc` used to be protected on the
grounds that disabling it is the most common way these tools break the exact thing they were
run for — which is true, and is now the first paragraph of
[its docs page](tweaks/services.xbox-accessory.md) instead of a refusal.

A fault is a service whose absence is an outcome nobody wanted and nobody connects back to a
program they ran once: no sound, no sign-in, no DHCP lease, no boot. Those stay refused. So does
the security stack — `BFE` is on the list because turning it off stops Windows Firewall
filtering while the firewall UI still reports itself as enabled, which is not a trade-off
anybody agreed to. Turning off the firewall or the antivirus is not an optimization, and a tool
that offers it as one is doing something other than what it says on the box.

`ServiceTweakTests.The_deny_list_still_covers_the_things_that_are_not_a_preference` pins the
floor, so the list can be trimmed further but not emptied by accident.

The list is enforced inside `WindowsServices.SetStartType` and `TryStop` — the lowest level
that touches the SCM — rather than in the catalog, so no future tweak can be written that
forgets to consult it. `WindowsServiceTweak` *also* checks it in its constructor, so a catalog
entry naming a protected service fails at startup instead of at apply time on somebody's
machine.

**Boot and System start types are refused.** The SCM will happily let you set a boot-start
driver to Disabled. The machine then does not come back and the fix is a recovery console. Both
the writer and the tweak's applicability check refuse anything at that scope.

**Capture records the exact prior state**, including the delayed-auto-start flag — `Automatic`
and `Automatic (Delayed Start)` are different settings that the SCM reports identically, and a
"restore defaults" would collapse them into one. Revert restores the captured start type and,
if it was delayed, the flag. It deliberately does not restart the service: a start type says
what should happen at the next boot, and starting a service that was stopped on purpose is a
larger intervention than a revert was asked to make.

**No service tweak is in any shipped profile.** Profiles are the "apply a set and get on with
it" path, and a service change is not something to hand somebody as part of a set. These are
opt-in, one at a time, after reading the page.

## Tweaks with options

Most tweaks do one thing. Some have a setting where several values are all defensible and the
right answer depends on the machine or on what the user is optimising for: how much CPU to
reserve for background work, how long to wait before resetting a hung GPU driver.

For those the tweak declares a `TweakChoice` — a named setting with a list of options, each
carrying its own description of what it does and what it costs. The UI renders them as radio
buttons with the descriptions visible (not a dropdown: collapsing the explanations behind a
click turns a choice back into a quiz), the CLI prints them under `nos show <id>` and accepts
`--set <choice>=<option>`, and profiles record the selection next to the tweak id.

The selection travels through `TweakContext.Options`, which already existed for parameterised
tweaks. Three consequences worth knowing:

- **`Read` resolves against the selection.** "Applied" means "matches the option you picked",
  not "matches some option" — so a machine sitting on Balanced while you have No reservation
  selected correctly reads as off.
- **`Capture` snapshots the union of every option's values**, not just the selected one.
  Otherwise applying one option and then another would leave the first option's writes outside
  the snapshot, and revert would miss them.
- **An unrecognised option throws** rather than falling back to the default. It means a profile
  or a command line named something that no longer exists, and quietly applying a different
  value is precisely the behaviour this project exists to avoid.
