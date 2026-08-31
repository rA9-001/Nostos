# services.sysmain

**Group:** Windows · **Improves:** Startup & Boot · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Shortens the gap between signing in and a machine that is actually ready, by removing work Windows schedules for itself in those first minutes.

## What it changes

The start type of the **`SysMain`** service -- Superfetch, as it was called before Windows 10 --
from `Automatic` to `Manual`, and stops the running instance.

## Mechanism, and how much it is worth

SysMain watches what you launch and preloads it into otherwise-unused RAM, so the next launch
comes from memory rather than from disk.

On a mechanical hard disk this was a large, easily demonstrated win, and that is where its
reputation comes from -- in both directions. On an SSD, and especially NVMe, the gap it was
closing is a fraction of what it used to be, so the prediction is worth much less. That much is
uncontroversial.

What does **not** follow, and what this entry is in the catalog for, is the claim that turning
it off makes games run better. The two arguments given for it are:

- *"It uses RAM."* It uses **standby** memory, which Windows hands back the instant something
  asks for it. A game does not run short of memory because SysMain filled the cache. Task
  Manager showing high memory use here is not showing you a problem.
- *"It causes disk activity."* It does, at low priority, mostly when the machine is idle. On
  some machines -- particularly a slow SATA SSD with a lot of file churn -- people report that
  activity being noticeable. On most it is not.

Microsoft's own guidance is to leave it on. This repo has no frametime data either way, so
The honest summary: **widely repeated, real mechanism, unproven effect on games.**

It is in the catalog anyway, because "leave it alone" is easier to accept from a tool that
explains why than from one that simply does not offer the option.

## When it might actually be worth it

If you can see `SysMain` doing sustained disk I/O in Task Manager while you are trying to play,
turn it off and see whether the stutter goes away. That is a real diagnosis. Turning it off
because a video said to is not.

## Trade-off

Cold application launches get slower -- noticeably on a SATA SSD, marginally on NVMe. That is a
real everyday cost paid against an unproven gaming benefit, which is why the recommendation here
is to leave this one alone unless you have a symptom.

## What "Manual" and "Disabled" actually mean

`--set start=<option>`, or the radio buttons in the app.

| Option | Start type | What it means |
| --- | --- | --- |
| `manual` | `SERVICE_DEMAND_START` | **Default and recommended.** The service no longer starts at boot, but anything that asks for it can still start it. |
| `disabled` | `SERVICE_DISABLED` | The service cannot start at all. Anything that needs it fails. |

This distinction is the most important thing on this page, and it is where tools in this
category do their damage. **Manual is a safety net.** If the reasoning below turns out to be
wrong for your machine -- some app you use genuinely needs this service -- Manual means it
starts on demand and you never find out there was a problem. Disabled means that app fails with
an error naming neither the service nor this tool, weeks after you ran it.

Pick `disabled` only if you have checked that the service keeps starting itself on Manual and
have decided you would rather it did not.

## What revert does

`nos revert <id>` restores the **exact** start type captured before the change, including the
difference between `Automatic` and `Automatic (Delayed Start)` -- two settings the SCM reports
identically and that a "restore defaults" would collapse into one.

The service is not restarted by revert. Its start type says what should happen at the next
boot, and starting a service that was deliberately stopped is a larger intervention than a
revert was asked to make.

## Why it is machine-scoped and needs elevation

Rewriting a start type is a `ChangeServiceConfig` call against the Service Control Manager,
which requires administrator rights. Through the background service that costs no prompt; from
a portable copy it needs an elevated launch.
