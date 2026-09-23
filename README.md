# Nostos

> *nóstos* (νόστος) — the homecoming. The Greek word for the journey back, and the root of
> *nostalgia*.

A Windows gaming optimizer that can prove what it changed, and put all of it back.

Most tools in this category apply folklore alongside real tweaks without distinguishing them,
"restore defaults" instead of restoring *your* prior values, and leave you no way to find out
what broke. This one is built the other way around.

[![build](https://github.com/rA9-001/Nostos/actions/workflows/ci.yml/badge.svg)](https://github.com/rA9-001/Nostos/actions/workflows/ci.yml)
[![release](https://img.shields.io/github/v/release/rA9-001/Nostos?sort=semver)](https://github.com/rA9-001/Nostos/releases/latest)
[![licence](https://img.shields.io/github/license/rA9-001/Nostos)](LICENSE)

## Install

**[Download the latest release](https://github.com/rA9-001/Nostos/releases/latest)** and run
`Nostos.exe` — the whole application in one 28 MB file. There is no installer and nothing
appears in Programs and Features. On first launch it installs a background service: one
administrator prompt, once, ever. Decline it and the app carries on in direct mode.

Windows warns on first run because the build is not code-signed yet. Every release publishes
SHA-256 hashes and a build attestation:

```
gh attestation verify Nostos.exe --repo rA9-001/Nostos
```

## How it is different

- **Every change is captured before it is made.** The prior value goes to an append-only
  journal on disk *before* the machine is touched, so a crash, a bluescreen or a power cut
  still leaves `nos revert --all` able to undo everything.
- **Every tweak carries an honest evidence rating** — `Measured` or `Plausible` — and every one
  is listed, always. Several are documented as probably doing nothing on your machine, because
  that is the truth.
- **Nothing touches a running game's memory.** No kernel driver, no injection, no hooking —
  only documented Win32 APIs from outside the process, so there is nothing for EAC, BattlEye or
  Vanguard to object to.
- **Nothing runs while you play.** No process watcher, nothing reacting to a game starting.
- **Gaming and Windows changes are kept apart.** Turning off the Fax service is a reasonable
  thing to do and a dishonest thing to sell as a framerate tweak.
- **Risky changes take a System Restore point first**, and services move to *Manual* rather than
  Disabled, so anything that turns out to need one still works.

## Status

Early, but usable end to end. The engine, journal, safety machinery, CLI, a catalog of 84
tweaks, the LocalSystem service and the desktop app are all done and tested. 520 tests, no
manual steps in CI.

## Building

Requires the .NET SDK. Full instructions, the CLI reference and the tweak-authoring guide are
in [docs/internals.md](docs/internals.md).

## Docs

| | |
| --- | --- |
| [internals.md](docs/internals.md) | Install detail, CLI, how a tweak works, layout, roadmap |
| [architecture.md](docs/architecture.md) | Engine, journal, service, drift reconciliation |
| [distribution.md](docs/distribution.md) | Code signing, attestation, releases |
| [uninstall.md](docs/uninstall.md) | Removing it, including by hand |
| [SECURITY.md](SECURITY.md) | What the program does and does not do |

## License

MIT. See [LICENSE](LICENSE).
