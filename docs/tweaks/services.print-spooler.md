# services.print-spooler

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`Spooler`** service - Print Spooler - from `Automatic` to `Manual`, and
stops the running instance.

## Mechanism, and an honest account of the benefit

The Print Spooler queues print jobs. It runs on every Windows install, whether or not a printer
has ever been attached.

**It does not cost you frames.** With no printer and nothing printing it sits idle, using a few
megabytes and no measurable CPU. Anyone telling you this one is a performance tweak is repeating
something they did not check.

The genuine reason people turn it off is **security**. The spooler runs as SYSTEM, is reachable
over the network, and has an unusually bad history of remote code execution bugs - PrintNightmare
in 2021 being the well-known one, but far from the only one. Microsoft's own guidance during that
period was to disable it on machines that do not print.

This repo has no category for attack surface. Earlier, that was treated as a reason to leave the
tweak out; that was the wrong call, because leaving it out does not stop anybody turning the
spooler off, it only stops them doing it somewhere that records what changed. So it is filed
under **Background & Cleanup** with its background-service neighbours, and this section is the
correction: the category is where it lives, not what it claims.

## How much this is worth, honestly

Rated on the performance claim, which is the claim it is usually recommended for, and which is
unsupported. The security argument is sound but is not what the rating measures.

## Trade-off

**You cannot print.** On `manual` the spooler starts on demand, so in practice printing still
works and simply starts a moment later - which is why Manual is the default here. On `disabled`
printing fails outright, and the error you get from most applications does not mention the
spooler.

Note that "print to PDF" also goes through the spooler.

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
