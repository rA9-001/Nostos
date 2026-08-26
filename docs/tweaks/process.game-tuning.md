# process.game-tuning

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Process · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

For one running process:

- **Priority class** → `High` (configurable with `--set priority=AboveNormal`)
- **EcoQoS participation** → explicitly opted out (`--set qos=efficiency` or `qos=system` to change)

Nothing is written to disk. The change dies with the process.

## Why this is the important one

This is the tweak that can be applied and undone **during a match**, because it touches no
stored configuration at all. It is also the safest thing in the catalog: the worst case is
that the process exits and everything is back to normal.

## EcoQoS

Windows 11's scheduler can place processes in an efficiency mode that prefers E-cores and
lower clocks. It is meant for background work, and it occasionally catches a foreground game —
particularly one launched by a launcher that was itself throttled. Opting out explicitly costs
nothing and removes the possibility.

`SetProcessInformation(ProcessPowerThrottling)` with `ControlMask = 0` clears the opinion
entirely, which is what revert uses — it restores *system-managed*, not a guessed default.

## Realtime priority is refused

`--set priority=RealTime` is rejected on purpose. Realtime outranks input, audio and the mouse
cursor; a game that saturates the CPU at realtime priority can make the machine unresponsive
enough to need a hard reset, and it has never been shown to improve frametimes.

## Anti-cheat

This uses `OpenProcess` with `PROCESS_SET_INFORMATION` and `PROCESS_QUERY_LIMITED_INFORMATION`
only. It never requests `PROCESS_VM_READ` or `PROCESS_VM_WRITE`, never injects, and never
hooks. There is nothing here for EAC, BattlEye or Vanguard to object to.

## Usage

```
nos apply process.game-tuning --process cs2 --set priority=High
nos revert process.game-tuning --process cs2
```

`--process` picks the largest-working-set match when a launcher spawns several processes with
the same image name.
