# stability.driver-search-off

**Group:** Gaming · **Improves:** Crashes & Freezes · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Fixes a specific fault that shows up while playing: driver timeouts, black screens, flicker. Repairs a broken machine rather than making a working one faster.

## What it changes

`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching`
`SearchOrderConfig` (REG_DWORD) -> `0`

`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`
`ExcludeWUDriversInQualityUpdate` (REG_DWORD) -> `1`

`HKLM\SOFTWARE\Policies\Microsoft\Windows\DriverSearching`
`DontSearchWindowsUpdate` (REG_DWORD) -> `1`
`DontPromptForWindowsUpdate` (REG_DWORD) -> `1`
`DriverUpdateWizardWuSearchEnabled` (REG_DWORD) -> `0`

The default for `SearchOrderConfig` is `1`; the rest are absent.

## Mechanism

Two separate paths deliver a driver you did not choose, and both have to be closed.

`SearchOrderConfig` is the old Device Installation Settings switch: `1` means "always install the
best driver software from Windows Update", `0` means never. It governs what happens when a
device is *installed* - plugged in, or enumerated for the first time.

`ExcludeWUDriversInQualityUpdate` is the modern policy, and it governs the other path: drivers
shipped as part of a monthly quality update to a device that is already working. This is the one
that replaces a GPU driver overnight.

The three under `Policies\...\DriverSearching` are the policy half of the first path.
`SearchOrderConfig` is a *preference*, and a preference is something Windows feels free to
re-derive - a feature update, a repair install, or a Settings page somebody clicked through can
put it back. The policy is not. Both, because either one alone leaves a path open.

Together they stop Windows Update sourcing drivers. They do not stop you installing drivers
yourself, and they do not stop the vendor's own updater.

## How much this is worth, honestly

`Plausible` as a stability measure, and this is one of the few entries in the catalog where the
failure it prevents is common enough that most people reading this have had it: a working
machine that developed a graphics fault, an audio device that changed behaviour, or a controller
that stopped being recognised, immediately after an update, because Windows replaced a
vendor-supplied driver with a generic one.

It buys no frames. It stops something from taking them away.

Filed under **Crashes & Freezes** for that reason - it is in the repair half of the catalog, not
the acceleration half.

## Trade-off

**You are now responsible for drivers.** Security fixes delivered through Windows Update for
drivers - and there have been serious ones, in graphics and in networking - will not arrive.
Neither will the driver for a device you plug in that has no in-box support, which will sit in
Device Manager as an unknown device until you find one yourself.

If you do not already update your GPU driver deliberately, this is a net negative. Apply it if
you pin driver versions on purpose; leave it alone if you do not.

## Revert

`nos revert stability.driver-search-off`. The captured state distinguishes "the policy value was
absent" from "it was zero", and revert restores whichever it was.

**A note on upgrading from 0.1.0.** This tweak wrote two values in 0.1.0 and writes five now.
Capture happens at apply time, so a journal entry written by the old build covers only the two
values it knew about. If you applied it on 0.1.0 and want the new three under your program's
control, revert it and apply it again.
