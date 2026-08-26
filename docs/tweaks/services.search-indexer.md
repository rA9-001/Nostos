# services.search-indexer

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`WSearch`** service (Windows Search), from `Automatic (Delayed Start)`
to `Manual`. The running instance is also stopped, so the change takes effect immediately
rather than at the next boot.

## Mechanism

Windows Search maintains a full-text index of your documents, mail and file metadata. Building
and updating that index is genuine disk and CPU work, and it is *event-driven*: it starts when
files change, not on a schedule you can predict. Installing a game, patching one, or moving a
large capture folder can all start indexing work that lands in the middle of whatever you do
next.

That unpredictability is why this is filed under Stutter and not FPS. The index does not run
constantly and is not stealing a steady slice of your framerate. It occasionally does a burst
of I/O at a moment nobody chose, and a burst of I/O at the wrong moment is a hitch.

## What you lose

**Start menu search keeps working.** It falls back to a slower, non-indexed scan. What you
notice:

- Searching *file contents* in Explorer becomes much slower, and on a large drive can take
  minutes instead of being instant.
- Outlook's search degrades badly -- the desktop Outlook client leans on this index heavily. If
  you use Outlook seriously, this tweak is not for you.
- Library and "Recent" views may populate more slowly.

If you mostly launch apps from the Start menu and rarely search inside documents, you will
probably not notice anything.

## Why "Plausible" and not "Measured"

The mechanism is not in question: the indexer does real I/O and Task Manager will show it doing
so. What is unmeasured is how often that I/O coincides with a game running and how large the
resulting frametime spike is, because that depends entirely on your drive and your file churn.
On an NVMe drive with a stable file set, plausibly never. Frametime captures during an indexing
pass would move this to Measured and would be a very welcome pull request.

## Trade-off

You are trading search quality for a smaller number of unscheduled I/O bursts. On a machine
where search matters that is a bad trade. This is one of the more genuinely contested entries in
the catalog, and the honest answer is that it depends on how you use the machine.

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
