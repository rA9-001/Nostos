# services.xbox-accessory

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

The start type of the **`XboxGipSvc`** service - Xbox Accessory Management Service - from
`Manual (Trigger Start)` to `Manual`, and stops the running instance.

## Read this first

**This is the service that makes an Xbox controller work.** Turning it off is the single most
common way tools in this category break the exact thing they were run for.

It was on this project's hard deny list until recently, and that was over-cautious. Plenty of
people play only on Steam with a mouse and keyboard, or with a DualSense, and for them this is a
service managing hardware they do not own. Refusing to offer it did not stop anybody turning it
off - it only pushed them towards `sc.exe`, where nothing is recorded and nothing can be put
back.

So it is offered, with this paragraph attached.

## Mechanism

`XboxGipSvc` handles the Xbox GIP protocol: enumeration, pairing, firmware updates and button
mapping for Xbox controllers and Xbox-branded accessories, wired and wireless.

Third-party controllers that present themselves as generic HID devices, and DualShock or
DualSense pads going through Steam Input, do not use it. Xbox controllers do.

## How much this is worth, honestly

There is no measured framerate benefit and no reason to expect one: the service is
trigger-started and idle until an Xbox accessory appears. Filed under **Background & Cleanup** with
the other background-service entries, and rated honestly - what you get is one fewer resident
service on a machine that has no use for it.

## Trade-off

**Xbox controllers stop working properly.** On `manual` the trigger can still start the service
when a controller is connected, which is the safety net that makes a wrong guess here
recoverable, and is why Manual is the default. On `disabled` the controller is either not
recognised at all or behaves erratically, with nothing anywhere connecting that to a service.

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
