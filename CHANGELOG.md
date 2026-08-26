# Changelog

Notable changes per release. Versions follow [semantic versioning](https://semver.org): while
the major version is `0`, a minor bump may change behaviour.

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
