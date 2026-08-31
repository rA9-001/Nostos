# Contributing

## Adding a tweak

Most tweaks are data. Add an entry to
`src/Nostos.Tweaks/Catalog/registry.json` and a page at `docs/tweaks/<id>.md`.

**Both are required.** CI fails a pull request that adds a tweak without a docs page, whose
docs page does not state the evidence rating the catalog claims, or whose docs page never
mentions the category it is filed under.

The docs page must cover:

0. **Which category it belongs to and why** — see below.
1. **Exactly what changes** — hive, key, value name, type, old and new value.
2. **The mechanism** — what Windows subsystem reads this value and what it does differently.
   "It makes things faster" is not a mechanism.
3. **Why the evidence rating is what it is.**
4. **The trade-off.** Everything costs something. If you cannot name the cost, you have not
   finished investigating.
5. **What revert does.**

## Adding text that people read

Every string a user sees comes from `src/Nostos.Core/Localization/en.json`, keyed by a dotted
name. Add the English there and refer to it:

- in a view, `Text="{loc:Tr settings.checknow}"`, or `{loc:TrFormat Key=..., Path=...}` when a
  value has to be substituted into it;
- in a view model, `Strings.Get("...")`, `Strings.Format("...", value)`, or
  `Strings.Plural("summary.count.change", n)` for anything counted.

**You do not have to translate it in the same commit.** A key missing from `de.json` falls back
to the English, and the app carries on. What you must not do is add a key to `de.json` that is
not in `en.json`, or give the two a different set of `{0}` placeholders — `StringTableTests`
fails the build for both, the second because a German string with a placeholder the English
does not have would throw at the moment it was displayed.

A tweak's own title and summary are translated in `src/Nostos.Tweaks/Catalog/de.json`, keyed by
tweak id, and are equally optional. `CatalogTranslationTests` checks the other direction: every
id translated there has to be a tweak that still exists, which is what catches a rename.

Dates are formatted from the table too, not from a `CultureInfo`. The build sets
`InvariantGlobalization`, so there is no culture data to ask and requesting one throws; use
`Strings.DateText`.

## Picking a category

`category` is one of exactly six values, and it is a claim about the result, not a note about
which part of Windows you edited. **Answer the group question first:**

> Does this change have a mechanism that reaches the game?

If you cannot describe that mechanism in one sentence without hedging, the answer is no, and
the tweak belongs in the **Windows** half. That is not a demotion — it is most of the catalog.

| Group | Id | Shown as | Pick it when the tweak… |
| --- | --- | --- | --- |
| Gaming | `performance` | Performance | frees CPU, GPU or memory the game can then use — the average goes up, the dips fill in, or both |
| Gaming | `input-lag` | Input Lag & Aim | shortens or steadies mouse and keyboard → screen |
| Gaming | `ping` | Ping | removes network delay, or delay Windows adds to network traffic |
| Gaming | `stability` | Crashes & Freezes | fixes a fault you hit while playing: a hang, a black screen, flicker |
| Windows | `interruptions` | Interruptions | stops something appearing over what you are doing, or restarting the box |
| Windows | `background` | Background & Cleanup | stops Windows running a service or feature you do not use |

An unrecognised value throws at construction, so a typo fails the build rather than quietly
creating a seventh category.

The rule that decides the hard cases: **would a user who filtered to this category and applied
everything in it feel misled?** If a tweak's honest benefit is "fewer popups", it goes in
`interruptions` however tempting `performance` looks. If it fixes a driver hang and changes
nothing about speed, it goes in `stability` — `gpu.tdr-delay` says so in its own summary.

Nearly every `services.*` tweak belongs in `background`. The one exception is
`services.delivery-optimization`, in `ping`, because saturating your uplink is a direct and
demonstrable cause of a ping spike. That is the bar for a service tweak to sit in the Gaming
half; "it uses some RAM" is not it.

Three consequences worth knowing when you add one:

- **Docs pages start with `**Group:**` and `**Improves:**`**, and CI checks both are there and
  match the catalog. Adding a tweak means deciding what you are claiming for it, in writing.
- **Every category must contain at least one tweak**, and both groups must be non-empty. If you
  are the last one out of a category, that is a conversation to have in the pull request, not
  something to leave as an empty filter.
- **The app and the CLI both list Gaming before Windows** and print a band between them, so a
  misfiled tweak is visible rather than buried.

If a tweak genuinely helps two categories, pick the one you can defend with a mechanism and say
so on the docs page. `graphics.hags` is filed under `input-lag` rather than `performance` for exactly
that reason: the submission-latency argument has a mechanism behind it and the framerate
argument mostly does not.

## Tweaks with options

If several values are all defensible and the right one depends on the machine or on what the
user is optimising for, do not pick one for them. Declare a choice:

```json
{
  "id": "example.tweak",
  "values": [],
  "choices": [
    {
      "id": "level",
      "title": "Reservation level",
      "description": "What this setting controls, in one sentence.",
      "default": "balanced",
      "options": [
        {
          "id": "balanced",
          "title": "Balanced - 10%",
          "description": "What it does AND what it costs. Who should pick it.",
          "recommended": true,
          "values": [ { "hive": "HKLM", "key": "...", "name": "...", "kind": "DWord", "value": "10" } ]
        }
      ]
    }
  ]
}
```

The tweak's own `values` are written under every option; each option adds its own on top. Put
the shared values in `values` and only the differences in the options.

Rules, all enforced by `CatalogIntegrityTests`:

- **At least two options.** One option is a value with extra ceremony.
- **The `default` must name an option that exists**, or every apply throws.
- **At most one `recommended`.** Two recommendations is not a recommendation.
- **Every option needs a real description** — currently at least 40 characters, and the intent
  is a sentence a person can decide on, not a restatement of the title. An option list without
  explanations is a quiz, and the whole reason for offering a choice is that the user cannot be
  expected to already know what `0` means here.

Say what an option **costs**, not just what it sets. If an option is a bad idea but people set
it anyway, offer it and say so plainly — doing it here means the prior value is captured and
`nos revert` works, which is better than them editing the registry by hand. See the `none`
option on `mmcss.system-responsiveness` for the tone.

Document the options in the tweak's docs page as a table, and check `nos show <id>` reads well.

For a native (C#) tweak, declare the same thing as `TweakChoice` objects in `Metadata.Choices`
and read the selection with `context.GetString("<choice id>")`. `GameProcessTuningTweak` is the
worked example.

## Evidence ratings

This is the part of the project that matters most. Be honest, and be conservative.

| Rating | Bar |
| --- | --- |
| `Measured` | Frametime data on real hardware, linked from the docs page. Or a mechanism whose cost is directly observable — a background workload that stops existing. |
| `Plausible` | Documented mechanism, plausible effect, **no data yet**. Most good tweaks start here. |
There used to be a third rating, `Folklore`, for changes that are widely repeated with no
demonstrated effect. It was removed: it had become a label the UI repeated on half the catalog
without telling anyone what to do about it, and the `nos list` filter built on it hid entries
from the exact people who had come looking for them.

**The argument it carried has not been dropped — it moved to where it is read.** `gpu.tdr-delay`
and `mmcss.games-task-priority` are still in the catalog and their pages still open by saying
they probably do nothing, under a *How much this is worth, honestly* heading. If your tweak is
one of those, write that section. A page that talks the reader out of applying it is a good
page.

**Ratings are meant to move.** If you have frametime data that promotes or demotes an entry,
that is one of the most valuable PRs you can send.

## Risk ratings

| Rating | Meaning |
| --- | --- |
| `Safe` | Reversible, no boot impact, no hardware interaction. |
| `Moderate` | Reversible but user-visible: battery life, background app behaviour, needs a reboot. |
| `Risky` | Can leave the machine unbootable or headless. A System Restore point is taken first. |
| `Experimental` | Unproven and potentially destabilising. Explicit opt-in only. |

Anything that requires a reboot must be at least `Moderate` — a bad outcome is not visible until
the machine comes back, and by then the person cannot see the page that warned them. CI enforces
this.

Note what a risk rating no longer buys you: there is no auto-revert. Nothing in this program
undoes a change on its own, so `Risky` means the docs page and the restore point are the whole
safety net. Write the page accordingly — say what the failure looks like and how to get out of
it from a machine in that state.

## Adding a service tweak

Services get their own rules, because this is where tools in this category do their worst
damage. A new one is a single entry in `src/Nostos.Tweaks/Catalog/services.json` plus a docs page —
no C#. The file's own header states the bar for being on the list.

**The bar is honesty, not benefit.** An earlier version of this page said a service whose only
real justification was "a smaller attack surface" or "less telemetry" did not belong in the
catalog, and gave Print Spooler as the example of one that was rejected. That test has been
dropped, because it did not work: leaving a tweak out does not stop anybody making the change,
it only stops them making it somewhere that records what the value was before. Rate it
`Plausible`, and let the docs page carry the honest argument about how much it is worth.

`services.print-spooler` is now in the catalog, and its page says in as many words that it will
not get you frames and that the genuine reason to do it is the spooler's history of remote code
execution bugs.

**Before proposing one, answer these in the pull request:**

1. **What does a player notice, and is that what you are claiming?** Pick the category the
   symptom belongs to, then make the page defend it. If the answer is "nothing, honestly, this
   is about telemetry", say that on the page under *How much this is worth, honestly*. Several
   existing pages open by telling you the tweak does nothing on a default install.
2. **What breaks?** Every service is load-bearing for somebody, and this is the part that is not
   negotiable. The docs page must name what stops working, specifically, near the top.
   `services.xbox-accessory` opens with the sentence "this is the service that makes an Xbox
   controller work", because that is the fact somebody needs before they click, not after.
3. **Have you checked the protected list?** `WindowsServices.Protected` will throw at
   construction — the build fails, not a user's machine. It now covers only services whose
   absence is a *fault* rather than a *choice*: boot, sign-in, networking, sound, and the
   security stack. Taking something *off* it is a discussion; adding something to it needs to
   clear that same line.

**Rules that are enforced, not suggested:**

- `manual` is the default and the only `Recommended` option. Tests assert this for every service
  tweak in the catalog. If you think a service should default to Disabled, you are proposing to
  change how the feature works, not adding an entry.
- Service tweaks are `Machine` scope, `Persistent` lifetime, and require elevation.
- A service tweak may appear in a shipped profile only if the thing it breaks is invisible to
  somebody who did not opt into it. `services.error-reporting` and `services.delivery-optimization`
  qualify: the worst case is a crash report that is not uploaded and an update that downloads
  from Microsoft instead of from a neighbour. Anything that can break a peripheral, a launcher or
  a save — the Xbox stack, Bluetooth, the spooler — does not, because a profile is applied by
  somebody who has read one description, not fifteen docs pages.

## Tweaks that need code

If a tweak needs to call an API, or its revert is not "put the old value back", it becomes a
class in `src/Nostos.Tweaks/Native/` implementing `ITweak`. That friction is
deliberate — declarative entries all share one well-tested implementation, and hand-written
revert logic is where bugs that break machines live.

Rules for native tweaks:

- `CaptureAsync` must record enough to restore the machine **exactly**, including "this did not
  exist before". If you cannot capture it honestly, the tweak does not belong in the catalog.
- `RevertAsync` must tolerate a stale snapshot and be idempotent. `UltimatePerformanceTweak`
  handles the case where the user's previous power scheme has since been deleted; yours must
  handle its equivalent.
- Clean up anything you created. Reverting should leave no trace.

## Hard constraints

**No kernel drivers. No process injection. No API hooking. No reading or writing another
process's memory.**

This is not negotiable and it is not about politeness — it is what keeps the project compatible
with EAC, BattlEye and Vanguard, and what keeps it distinguishable from a cheat. Every tweak
must work through documented Win32 APIs from outside the target.

If you believe something genuinely requires crossing that line, open an issue first. The answer
will almost certainly be no.

**No bundled extras.** No telemetry, no "cleaner", no auto-updater that phones home, no offers.
See [docs/distribution.md](docs/distribution.md) for why this is a distribution requirement and
not just taste.

**All JSON goes through a source-generated context.** `JsonSerializer.Serialize(value)` and
`Deserialize<T>(json)` without a `JsonTypeInfo` use reflection to emit code at runtime, which
the ahead-of-time build cannot do. Every format has a context already — `JournalJsonContext`,
`ProfileJsonContext`, `PendingChangeJsonContext`, `CatalogJsonContext`, `IpcJsonContext`,
`ServiceConfigurationJsonContext` — so adding a field usually needs nothing. A new top-level
type needs a `[JsonSerializable]` entry. CI catches the rest: the one-file build fails on
IL2026/IL3050 rather than shipping something that breaks at runtime.

There is one trap here that is silent, and it has bitten this codebase once already. **A record
with any `required` member is built from a single argument list, so its property initializers do
not run during deserialization** — `= []` comes back null and `= "manual"` comes back null. Use
a null-coalescing init accessor on any optional property that has a default:

```csharp
public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];
```

`src/Nostos.Core/Json/CoreJson.cs` explains it at length, and
`CatalogParsingTests` guards it.

## Building and testing

```
dotnet build
dotnet test
.\scripts\publish.ps1 -Portable -Output dev    # writes dev\Nostos.exe
```

`dev\Nostos.exe` is the build to double-click while working. The publish takes a few
seconds because it is a plain folder build, not the ahead-of-time one.

Do not test a catalog change by running `dotnet run --project src/Nostos.App` on a
machine that has the service installed — see the next section for why it will lie to you.
`scripts\dev.ps1` runs the same thing directly out of `bin\` and can drive the CLI
(`-Cli list --all`), which is quicker when you only need to see whether an entry reads.

## Testing the service path

When the app can reach the service, **the service supplies the catalog**. `SplitBackend` asks it
for the whole status list and then re-reads only the user-scoped rows in-process, because
LocalSystem has its own user hive and would report your HKCU tweaks against the wrong one.

The consequence is easy to trip over and hard to diagnose: a freshly built app talking to a
service built last week shows *last week's tweaks*. It looks exactly like a build that did not
pick up your change, and the app gives no hint, because from its side nothing is wrong.

So there are two loops, and it is worth knowing which one you are in.

**Everything except the service itself** — catalog entries, options, wording, the window, read
and revert logic:

```
.\scripts\publish.ps1 -Portable -Output dev
dev\Nostos.exe
```

Run it as administrator if the tweak is machine scope — portable mode has no privileged service
to hand that work to, so unelevated it reports those as skipped.

**The service path** — drift reconciliation, applying machine-scope tweaks from an unelevated
session, or any change under `src/Nostos.Service`. This replaces the
installed binaries, so it needs an elevated shell:

```powershell
sc.exe stop Nostos
.\scripts\publish.ps1 -Output dist
sc.exe start Nostos
```

`publish.ps1` refuses to write over a service that is still running rather than failing halfway
and leaving a folder with two versions in it, so the stop is not optional.

`Core` must stay free of Windows dependencies and NuGet packages. If your change adds either,
it probably belongs in `Win32` or `Tweaks`.

Avalonia, in `App`, is the only third-party dependency in the product, and it should stay that
way. Nothing that runs privileged — engine, service, interop — takes a package: that code has to
be short enough for a reviewer to read in full, and a small binary is also less likely to trip
the AV heuristics this category of tool attracts anyway.

Engine changes need tests in `tests/Nostos.Core.Tests` using `FakeTweak` — particularly
for failure paths. The interesting cases are the ones that are hard to produce on real hardware:
apply throws mid-mutation, revert throws too, a torn journal line, drift after Windows Update.

## Testing tweaks on a real machine

Use a VM with a checkpoint, or at minimum:

```
nos apply <id> --dry-run     # see what would change
nos apply <id>
nos status <id>              # confirm it reads back
nos revert <id>
nos status <id>              # confirm the machine is genuinely back
```

The last step is the one people skip. A tweak whose revert does not restore the original state
is a bug in the same class as a tweak that bluescreens.
