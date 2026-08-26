# Nostos

> *nóstos* (νόστος) — the homecoming. The Greek word for the journey back, and the root of
> *nostalgia*.

[![build](https://github.com/rA9-001/Nostos/actions/workflows/ci.yml/badge.svg)](https://github.com/rA9-001/Nostos/actions/workflows/ci.yml)
[![release](https://img.shields.io/github/v/release/rA9-001/Nostos?sort=semver)](https://github.com/rA9-001/Nostos/releases/latest)
[![licence](https://img.shields.io/github/license/rA9-001/Nostos)](LICENSE)

A Windows gaming optimizer that can prove what it changed, and put all of it back.

Most tools in this category share three problems: they apply folklore alongside real tweaks
without distinguishing them, they "restore defaults" instead of restoring *your* prior values,
and they leave you with no way to find out what broke. This one is built the other way around.

- **Every change is captured before it is made.** The prior value goes to an append-only
  journal on disk *before* the machine is touched, so a crash, a bluescreen or a power cut
  still leaves `nos revert --all` able to undo everything.
- **Every tweak carries an honest evidence rating**, `Measured` or `Plausible`, and every one
  is listed, always — nothing is hidden for being poorly evidenced. Several entries are
  documented as probably doing nothing on your machine, because that is the truth: each page
  has a *How much this is worth, honestly* section, and some of them talk you out of it.
- **Nothing touches a running game's memory.** No kernel driver, no injection, no hooking.
  Everything works through documented Win32 APIs from outside the process, so there is nothing
  for EAC, BattlEye or Vanguard to object to.
- **Nothing runs while you play.** There is no process watcher: nothing enumerates your running
  programs and nothing reacts to a game starting. You optimise, then you play. A background
  service that watches for games and reaches into them is hard to tell apart from something
  malicious, and it is not worth the risk to your account to save one click.
- **Gaming changes and Windows changes are kept apart.** Two groups, six categories:
  *Performance*, *Input Lag & Aim*, *Ping* and *Crashes & Freezes* are filed under **Gaming**
  and have to have a mechanism that reaches the game; *Interruptions* and *Background &
  Cleanup* are filed under **Windows** and do not claim to. Turning off the Fax service is a
  reasonable thing to do and a dishonest thing to sell as a framerate tweak.
- **The service optimizer tells you which one breaks your controller.** It moves services to
  *Manual*, not Disabled, so anything that turns out to need one still works. Thirty-five services
  are offered, one tweak each, one docs page each, no "disable 40 services" button — and the
  page for `XboxGipSvc` opens by telling you it is what makes an Xbox controller work. What the
  tool still refuses, in the code that talks to the Service Control Manager rather than in a
  config file, is the set whose absence is a fault rather than a choice: `Audiosrv`, `RpcSs`,
  `Dhcp`, `BFE` and the rest of the boot, sign-in, sound and security stack.
- **Where several values are all defensible, you pick.** Tweaks that have a real trade-off
  offer the options with an explanation of what each one costs, rather than hiding a number
  somebody chose for you.
- **Risky changes take a System Restore point first.** Anything that could leave the machine
  headless gets a restore point before it is applied. Nothing then undoes itself: what you
  apply stays applied until you revert it, in the app or with `nos revert`.
- **It can measure, and it will tell you when it cannot.** `nos bench` reports median, p95, p99
  and jitter rather than an average, and `nos bench compare` bootstraps a confidence interval
  before it will call a difference real. Most tweaks come back "no measurable difference", which
  is the honest answer — see [docs/benchmark.md](docs/benchmark.md).

## Status

Early, but usable end to end. The engine, journal, safety machinery, CLI, a catalog of 84
tweaks, the LocalSystem service and the desktop app are all done and tested. 520 tests, no
manual steps in CI.

## Getting started

**[Download the latest release](https://github.com/rA9-001/Nostos/releases/latest)**, then run
`Nostos.exe`. That is the whole setup.

| Download | What it is |
| --- | --- |
| `Nostos.exe` | The whole application in one 28 MB file. Nothing to install, nothing to unpack. Start here. |
| `Nostos-x.y.z-win-x64.zip` | The folder build: app, `nos` CLI, and the background service that lets machine-wide tweaks apply without a prompt each time. |

There is no installer and nothing appears in Programs and Features. The one thing that does get
installed is the background service, and the app registers that itself on first launch.

Windows will warn you on first run, because the build is not code-signed yet — see
[docs/distribution.md](docs/distribution.md) for why, and
[SECURITY.md](SECURITY.md) for what the program does and does not do. Every release publishes
SHA-256 hashes and a build attestation, so you can check what you downloaded:

```
gh attestation verify Nostos.exe --repo rA9-001/Nostos
```

On first launch the app:

1. creates its data folder,
2. installs the default profiles if you have none,
3. installs and starts the background service — **one administrator prompt, once, ever**,
4. connects and loads the catalog.

If you decline the prompt, it carries on in direct mode and never asks again; an **Enable**
button in the banner is there whenever you change your mind. Nothing else is required, and
there is nothing to configure before it works.

The setup looks like nothing happened, which is the point. To check, run `nos service status`.

Everything the app writes outside its own folder lives in one place,
`%ProgramData%\Nostos`. `nos revert --all` undoes the changes, and
`Nostos.Service.exe uninstall` from an elevated prompt removes the service.

### Giving it to someone else

Send them the [release page](https://github.com/rA9-001/Nostos/releases/latest) and tell them to
take `Nostos.exe`. One file, nothing to explain, and they get updates from then on.

If you are handing over a build you made yourself: send the whole folder or the `.zip` the
publish script produces. What does *not* work is picking `Nostos.exe` out of `dist\` and sending
only that — in a folder build, that file is a 160 KB launcher and the application is the
assemblies beside it.

Either way the build is self-contained, so the recipient needs nothing installed. They will get
a SmartScreen warning on first launch because it is unsigned; see
[docs/distribution.md](docs/distribution.md) for why, and for what is being done about it.

### Portable, in one file

There is a build that is a **single 27 MB `Nostos.exe`** and nothing else. No runtime
to install, no folder of DLLs, no service, no registry entry. Copy it to a USB stick and run it.

```
.\scripts\publish.ps1 -SingleFile -Output portable
```

It is compiled ahead of time, so it starts in under a second. On first run it creates a `data`
folder beside itself holding the journal, the profiles, the logs, and the three C++ rendering
libraries that cannot be linked into a managed executable — about 18 MB, unpacked once and
reused after that. Delete the folder and the app rebuilds it; delete both and nothing of the
app remains.

Portable mode is deliberately limited, and the app says so in a banner:

- **No background service**, so no drift reconciliation.
- **Machine-wide tweaks need an elevated launch**, because there is no privileged helper.
- **It will not use an installed service even if one is running on the machine.** That would
  work, but the service journals to `%ProgramData%`, so half the record of what this copy
  changed would live somewhere the folder does not carry with it.

Any ordinary folder build can also be made portable by putting an empty `portable.txt` next to
the executable, or passing `--portable`.

### Updating

The app checks GitHub on launch and shows a banner when there is a newer release. One click
downloads and installs it; nothing is fetched until you click. From the CLI:

```
nos update              # check only
nos update --install    # download, verify and install
```

**Every update is verified before it is installed.** Each release publishes a `SHA256SUMS.txt`
and an ECDSA P-256 signature over it, and the public key is compiled into the application. The
signature must verify and the download's hash must match, or nothing is written — see
[SECURITY.md](SECURITY.md#updates). This matters more than it usually would: part of what gets
installed runs as LocalSystem.

Updating the single-file build needs no prompt. Updating a folder build needs one, because the
service has to be stopped to replace it.

### Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/rA9-001/Nostos
cd Nostos
dotnet build
.\scripts\publish.ps1 -Portable -Output dev
```

That last line writes **`dev\Nostos.exe`** — an ordinary executable you double-click.
It takes a few seconds, and it is the one to run while working on something.

```
.\scripts\publish.ps1 -Portable -Output dev     # refresh it after a change
dev\Nostos.exe                         # run it
```

The `-Portable` marker is the part that matters. If this machine has the service installed, an
app that can reach it asks the *service* for the tweak list and shows whatever catalog the
service was built with — so a change you just made appears not to have taken, with no hint that
anything is wrong. That applies to `dotnet run --project src/Nostos.App` too. Portable
mode ignores the service, and keeps the journal in `dev\data` instead of mixing test applies
into the real record.

`scripts\dev.ps1` does the same thing straight from `bin\` without publishing, and can run the
CLI:

```
.\scripts\dev.ps1 -Cli list --all               # check the catalog without opening the window
.\scripts\dev.ps1 -Elevated                     # machine-scope tweaks can actually apply
```

Only the service path itself — drift reconciliation, and machine-scope changes without an
elevated shell — needs a real publish over the install. See
[CONTRIBUTING.md](CONTRIBUTING.md#testing-the-service-path).

To produce a distributable folder with all three executables together:

```
.\scripts\publish.ps1 -Zip                       # dist\ + dist.zip, about 128 MB / 51 MB
.\scripts\publish.ps1 -SingleFile               # one 27 MB exe, app only, portable
.\scripts\publish.ps1 -FrameworkDependent        # about 53 MB, needs the .NET 10 runtime
.\scripts\publish.ps1 -Portable -Output usb      # writes portable.txt into the output
```

`-SingleFile` compiles ahead of time and needs the MSVC linker — install Visual Studio's
"Desktop development with C++" workload. It takes a few minutes; the other modes take seconds.

If PowerShell refuses to run the script, Windows is at its default execution policy:
`powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip`.

All three executables share one set of runtime assemblies, so publishing them into a single
folder costs barely more than publishing one. The script refuses to publish over an installed
service that is currently running from the output folder, which would otherwise fail halfway
and leave a mixed-version folder behind.

Self-contained is the default because a runtime download is a second thing for the recipient to
install and a second thing to get wrong. The script publishes with `-r win-x64`; without a
runtime identifier, publish pulls in Avalonia's native assets for every platform and the output
balloons past half a gigabyte.

### The CLI

`nos.exe` does everything the window does, and a few things it does not.

```
nos list                        # the catalog, grouped by what it improves
nos categories                  # the categories, by group, and what each one claims
nos list --category ping        # only the tweaks that claim to help your ping
nos list --all                  # everything, including the stuff that probably does nothing
nos status                      # what is set on this machine right now
nos apply mmcss.system-responsiveness --dry-run
nos revert --all                # undo everything this tool has ever done here
nos journal                     # the full change log
nos doctor                      # environment report, for bug reports
```

Tweaks that offer a choice:

```
nos show mmcss.system-responsiveness              # the options, and what each one costs
nos apply mmcss.system-responsiveness --set reserve=balanced
nos apply gpu.tdr-delay --set delay=windows-default
```

Against a running process, when you want it:

```
nos apply process.game-tuning --process cs2 --set priority=high --set qos=high
nos revert process.game-tuning --process cs2
```

Add `--service` to route a command through the service, which avoids needing an elevated shell
for machine-scope tweaks. See [docs/service.md](docs/service.md).

## How a tweak works

Every tweak — registry, power scheme, live process, whatever comes later — implements the same
five operations:

| | |
| --- | --- |
| `ReadAsync` | What is the machine set to right now? |
| `CaptureAsync` | Record the prior value so revert can restore *exactly* it |
| `ApplyAsync` | Make the change |
| `RevertAsync` | Put back the captured value |
| `VerifyAsync` | Did it actually stick? |

If a tweak cannot implement `CaptureAsync` honestly, it does not go in the catalog.

Apply always runs in this order, and the order *is* the safety property:

```
check applicability -> read state -> capture prior value -> journal the intent
                                                                   |
                                                       (durable before destructive)
                                                                   |
                                                    mutate -> verify -> journal the result
```

Everything after the journal write is recoverable. If `ApplyAsync` throws, the engine
immediately reverts using the snapshot it already has; if *that* fails too, it says so plainly
rather than pretending the machine is clean.

## Adding a tweak

Most tweaks are data, not code. Add an object to
[`src/Nostos.Tweaks/Catalog/registry.json`](src/Nostos.Tweaks/Catalog/registry.json):

```json
{
  "id": "category.what-it-does",
  "title": "Human readable",
  "summary": "One sentence on what changes and what it costs.",
  "category": "fps",
  "scope": "Machine",
  "risk": "Safe",
  "evidence": "Plausible",
  "values": [
    { "hive": "HKLM", "key": "SOFTWARE\\...", "name": "ValueName", "kind": "DWord", "value": "10" }
  ]
}
```

…and a page at `docs/tweaks/<id>.md` explaining the mechanism and justifying the evidence
rating. **CI fails without the docs page.** That is the whole point.

`category` is one of six fixed values — `fps`, `stutter`, `input-lag`, `ping`, `interruptions`,
`stability` — and it is a claim, not a filing cabinet. It says what the tweak does *for the
player*, not which part of Windows it writes to, which is why two HKCU values can end up in
different categories and a registry key and a power scheme can end up in the same one. An
unrecognised category fails the build, and a docs page that never mentions its own category
fails CI. `nos categories` prints what each one promises.

Tweaks that need to call an API, or whose revert is not "put the old value back", become a
class in `src/Nostos.Tweaks/Native/` instead. That friction is deliberate.

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Layout

```
src/Nostos.Core/     Engine, journal, profiles, safety. No Windows dependency, fully unit-tested.
src/Nostos.Win32/    Registry, power schemes, process control, P/Invoke.
src/Nostos.Tweaks/   The catalog: declarative JSON + native tweaks.
src/Nostos.Ipc/      Control-pipe contract and client. Portable, no Windows dependency.
src/Nostos.Service/  LocalSystem service: ACL'd pipe, reconciler.
src/Nostos.Cli/      `nos`.
src/Nostos.App/      Avalonia desktop app. Bootstraps itself on launch; talks to the service or the engine.
tools/                        Maintainer tooling. Release signing. Never shipped.
tests/                        495 tests, including catalog integrity rules enforced in CI.
docs/tweaks/                  One page per tweak. Required.
profiles/                     Sample profiles.
```

`Core` deliberately has no Windows dependency and no NuGet packages, so the logic that decides
what happens to your machine is testable on any runner and readable without a Windows SDK.

Avalonia, used by the app, is the **only** third-party dependency in the product. Everything
that runs privileged — the engine, the service, the interop — has none, so the code that can
change your machine is short enough to read in full.

## Roadmap

- [x] Tweak engine with capture/apply/revert/verify and a crash-safe journal
- [x] System Restore integration before risky changes
- [x] Declarative registry catalog + native tweaks
- [x] CLI
- [x] `Nostos.Service` — LocalSystem service, SID-ACL'd named-pipe API, drift
      reconciliation
- [x] Desktop app (Avalonia), talking to the service so there is exactly one UAC prompt per install
- [x] Single-file portable build, compiled ahead of time
- [x] Latency benchmark with a bootstrap comparison (`nos bench`)
- [x] Signed releases and an in-app updater that verifies them
- [ ] Frametime capture via ETW, without injecting into anything — see docs/architecture.md
- [x] Per-tweak options with an explanation of each choice, in the app and the CLI
- [ ] Impersonate the console session so the service can apply user-scoped (HKCU) tweaks
- [ ] Instance-keyed journal entries, so one tweak can be outstanding more than once
- [ ] A target-process picker in the app, for process-scoped tweaks
- [ ] ETW frametime capture, so `Plausible` entries can be promoted to `Measured` with data
- [ ] Service control tweaks, NIC properties, per-executable fullscreen-optimization flags
- [ ] Signed releases via [SignPath Foundation](https://signpath.org/) — see
      [docs/distribution.md](docs/distribution.md)

## A word on expectations

Some of what circulates as gaming optimization is real, some is measurable but tiny, and some is
pure folklore that has been copied between forum posts for fifteen years. This project's job is
to tell you which is which, apply what you choose, and be able to undo all of it.

If a page here says a tweak probably does nothing and you have frametime data showing
otherwise, open a PR. Ratings are meant to move.

## Licence

MIT. See [LICENSE](LICENSE).
