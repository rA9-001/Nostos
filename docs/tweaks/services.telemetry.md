# services.telemetry

**Group:** Windows · **Improves:** Telemetry & Privacy · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows collecting, identifying and uploading what you do. Nothing here is about speed, and none of it is a substitute for the privacy settings Windows already offers.

## What it changes

The start type of **`DiagTrack`** -- "Connected User Experiences and Telemetry" -- from
`Automatic` to `Manual`, and stops the running instance.

## Mechanism, and how much it is worth

DiagTrack collects diagnostic and usage data, batches it to disk under
`%ProgramData%\Microsoft\Diagnosis`, and uploads it periodically.

The work is small, batched, and mostly scheduled for idle time. The claim that stopping it
raises your framerate is one of the most widely repeated in this whole category and one of the
least supported: nobody has produced frametime data showing it, and the mechanism -- occasional
batched writes and an HTTPS upload -- is not the shape of something that costs frames.

It is in the catalog for exactly that reason, and it stays in the catalog because the honest
reason to turn it off is a different one.

## The honest reason to turn it off

**Privacy, not performance.** If you would rather Windows did not send diagnostic data, this is
the service that sends it, and turning it off is a reasonable thing to want. That is a
legitimate motivation and this tool will help you act on it -- it will just not pretend you are
buying frames.

This is also a blunt instrument compared with Settings, Privacy, Diagnostics & feedback, which
is the supported way to reduce what is collected.

## What you lose

- Windows Update and the Store may make slightly worse decisions about what to offer you, since
  those use telemetry signals.
- Feedback Hub stops working properly.
- On a managed or corporate machine this may conflict with policy. Check before running it.

## Trade-off

Very little day to day. The cost of this entry is not what it breaks, it is that turning it off
feels like an optimisation when it mostly is not. Read the rating.

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
