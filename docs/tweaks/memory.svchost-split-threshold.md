# memory.svchost-split-threshold

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control`
`SvcHostSplitThresholdInKB` (REG_DWORD)

Windows ships `0x380000`, which is 3,670,016 KB - exactly 3.5 GB. The options write that or
`0xFFFFFFFF`.

## Mechanism

Since Windows 10 1703, the SCM compares this number against the machine's physical RAM in
kilobytes when it decides how to host services:

- **RAM above the threshold** - every service that can be split gets its own `svchost.exe`.
- **RAM below the threshold** - services are grouped into shared `svchost.exe` processes, the
  way Windows 7 did it.

Every desktop built in the last decade has more than 3.5 GB, so every desktop is in the first
case, which is why Task Manager shows forty-odd `svchost.exe` entries. Setting the threshold to
`0xFFFFFFFF` puts every machine in the second case.

Grouping saves per-process overhead: each `svchost.exe` carries its own address space, thread
pool, loader state and working set, and thirty of them cost more than five do. The saving is
tens to low hundreds of megabytes, depending on what is installed.

## How much this is worth, honestly

`Plausible` for the memory, unsupported for anything else. You can watch the process count fall
and the committed memory drop in Task Manager, so the mechanism is not in doubt. What is in
doubt is whether a machine with 16 or 32 GB cares, and the honest answer is that it does not.

The split exists for a reason, and the reason is the trade-off below. Microsoft moved *towards*
isolation on purpose; this moves back.

## Trade-off

**You lose fault isolation.** With services grouped, an unhandled exception in one takes down
the process and every service sharing it. That failure mode - several unrelated things dying at
once, with an event log entry naming only `svchost.exe` - is exactly what the split was
introduced to stop, and it is genuinely harder to diagnose than a single service failing.

You also lose per-service resolution in Task Manager: memory and CPU are attributed to the
group, not to the service inside it.

Services with hard-coded grouping requirements stay grouped either way, so this is not a
complete reversal in either direction.

Needs a reboot: the SCM reads the threshold when it starts.

## Revert

`nos revert memory.svchost-split-threshold`, then reboot.
