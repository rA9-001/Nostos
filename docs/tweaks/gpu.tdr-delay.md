# gpu.tdr-delay

**Group:** Gaming · **Improves:** Crashes & Freezes · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Fixes a specific fault that shows up while playing: driver timeouts, black screens, flicker. Repairs a broken machine rather than making a working one faster.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers`
`TdrDelay` (REG_DWORD)

Default is `2` seconds. **You choose the value.**

## Options

`--set delay=<option>`, or the radio buttons in the app's detail pane.

| Option | Value | What it means |
| --- | --- | --- |
| `windows-default` | 2 | The shipped timeout. A hung driver is reset quickly, so a genuine fault is a brief flicker rather than a long freeze. |
| `extended` | 8 | **Default of the three.** Tolerates long shader compilation without a spurious reset; a real hang freezes the screen for eight seconds instead of two. |
| `compute` | 60 | For GPU compute kernels that legitimately take a minute. Wrong for gaming. |

None of these makes anything faster. Read the next section before picking one.

## What it actually does

Timeout Detection and Recovery is the watchdog that resets the GPU when the driver stops
responding. `TdrDelay` is how long Windows waits before deciding the GPU is hung.

## How much this is worth, honestly

Raising `TdrDelay` **does not make anything faster.** It is recommended constantly on the basis
that it "fixes stutter", which it cannot do — it only changes how long the machine sits frozen
before recovering from a driver hang instead of recovering after 2 seconds.

It is genuinely useful in one narrow case: long-running GPU compute kernels that legitimately
exceed the timeout. For gaming, a machine that hits TDR at all has a real problem — bad
overclock, failing card, driver bug — and extending the timeout hides the symptom.

It is in the catalog because people apply it anyway, and journaled-and-revertible is better
than a hand-edited registry with no record. It is hidden unless you pass `--all`.

## Revert

`nos revert gpu.tdr-delay`, then reboot.
