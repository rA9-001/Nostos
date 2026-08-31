# services.sensors

**Group:** Windows · **Improves:** Unused Features · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Turns off features this PC has but you do not use -- Bluetooth, printing, Xbox and Game Pass, Fax. You already know which of these you need; each one names what stops working.

## What it changes

The start type of the **`SensorService`** service - Sensor Service - from `Automatic` to `Manual`, or from
whatever it currently is if something has already changed it, and stops the running
instance.

The service is not registered on every edition or every build. Where it is absent the
tweak reports itself as not applicable rather than failing.

## Mechanism

`SensorService` monitors the ambient light sensor, accelerometer, gyroscope and magnetometer,
and publishes their readings to anything that asks - including the shell, which uses them for
adaptive brightness and automatic screen rotation.

## How much this is worth, honestly

On a desktop tower there are no sensors, so the service starts, enumerates nothing and stops
being interesting. The honest claim is the same as the rest of the hardware family: it is not
costing you frames, it is just a service with nothing to do.

On a laptop or a convertible it does have something to do, and this is the wrong tweak.

## Trade-off

Automatic screen rotation stops. Adaptive brightness stops. Anything that reads device
orientation - a few games use it on tablets - stops getting readings.

Nothing on a desktop with a fixed monitor notices.

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
