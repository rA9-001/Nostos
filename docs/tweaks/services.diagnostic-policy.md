# services.diagnostic-policy

**Group:** Windows · **Improves:** Telemetry & Privacy · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows collecting, identifying and uploading what you do. Nothing here is about speed, and none of it is a substitute for the privacy settings Windows already offers.

## What it changes

The start type of the **`DPS`** service - Diagnostic Policy Service - from `Automatic` to
`Manual`, and stops the running instance.

## Mechanism

DPS hosts the Windows troubleshooters: "diagnose network problems", the audio troubleshooter,
the Windows Update troubleshooter. It also collects some diagnostic data for the built-in
diagnostic policies.

It is genuinely idle almost all of the time. When it does run, it runs scripts.

## How much this is worth, honestly

Because "it uses RAM in Task Manager" is the whole argument, and it is not a good one. DPS sits
in a shared `svchost` doing nothing measurable until a troubleshooter is invoked. Nobody has
demonstrated a frametime difference, and the mechanism does not suggest there would be one.

It is filed under **Background & Cleanup** because that is the category the claim belongs to - the
claim being that background diagnostic work causes hitching - and this page is where that claim
gets marked unproven.

## Trade-off

The Windows troubleshooters stop working. On `manual` they will start on demand when you launch
one, so in practice you lose nothing; on `disabled` "Troubleshoot" buttons throughout Settings
stop doing anything, with an error that does not explain why.

This is a good example of a tweak where Manual costs you nothing at all and Disabled costs you
something you will not connect back to this.

## What "Manual" and "Disabled" actually mean

`--set start=<option>`, or the radio buttons in the app.

| Option | Start type | What it means |
| --- | --- | --- |
| `manual` | `SERVICE_DEMAND_START` | **Default and recommended.** The service no longer starts at boot, but anything that asks for it can still start it. |
| `disabled` | `SERVICE_DISABLED` | The service cannot start at all. Anything that needs it fails. |

**Manual is a safety net.** If the reasoning on this page turns out to be wrong for your machine,
Manual means the service starts on demand and you never find out there was a problem. Disabled
means whatever needed it fails with an error naming neither the service nor this tool, weeks
after you ran it.

Pick `disabled` only after checking that the service keeps starting itself on Manual and
deciding you would rather it did not.

## What revert does

`nos revert <id>` restores the **exact** start type captured before the change, including the
difference between `Automatic` and `Automatic (Delayed Start)` - two settings the SCM reports
identically and that a "restore defaults" would collapse into one.

The service is not restarted by revert. Its start type says what should happen at the next boot,
and starting a service that was deliberately stopped is a larger intervention than a revert was
asked to make.

**Machine-scoped**, so it needs elevation: through the background service that costs no prompt,
from a portable copy it needs an elevated launch.
