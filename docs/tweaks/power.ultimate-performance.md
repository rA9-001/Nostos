# power.ultimate-performance

**Group:** Gaming · **Improves:** Performance · **Risk:** Moderate · **Evidence:** Measured · **Scope:** Machine · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

Activates the **Ultimate Performance** power scheme
(`e9a42b02-d5df-448d-aa00-03f14749eb61`), unhiding it first if the machine has never used it.

## Mechanism

Ultimate Performance is High Performance with two additional changes that matter for frametimes:

- **Core parking disabled.** Parked cores take time to come back online. A frame that suddenly
  needs a parked core waits for it, which shows up as a frametime spike rather than a lower
  average FPS.
- **Idle latency tolerance reduced.** The processor spends less time in deep C-states, so
  wake-up latency drops.

Both are documented behaviours of the scheme, and the frametime effect of core parking on
lightly-threaded workloads is well established.

## Trade-off, and why laptops are refused

Idle power draw goes up substantially. On a battery-powered machine this is a straight
regression, so `CheckApplicability` refuses to run it when a battery is present. Override with
`--set allowOnBattery=true` if you know what you are doing.

## What revert does

Restores the power scheme that was active when the tweak was applied, and — only if the
optimizer was the thing that unhid Ultimate Performance — deletes it again so it does not
linger in the user's power menu.

If the previously active scheme has since been deleted, revert falls back to Balanced and says
so, rather than leaving the machine on Ultimate Performance.

## Revert

`nos revert power.ultimate-performance`.
