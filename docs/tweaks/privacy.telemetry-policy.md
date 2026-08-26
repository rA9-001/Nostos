# privacy.telemetry-policy

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`
`AllowTelemetry` (REG_DWORD) -> `0`

The value does not exist on a clean install.

## Mechanism

The policy sets the diagnostic data level the whole machine reports at. The documented levels
are `0` Security, `1` Basic/Required, `2` Enhanced, `3` Full/Optional.

Here is the part every "disable telemetry" guide omits: **level 0 is only honoured on Enterprise,
Education and IoT editions.** On Home and Pro the effective floor is Required. Writing `0` on a
Pro machine gets you Required, exactly as if you had written `1`.

So the accurate description of this tweak is "set diagnostic data to the lowest level this
edition permits", which is what the title says.

It is worth pairing with `services.telemetry`, which stops the service that does the uploading,
and understanding that the two are different things: this sets policy, that stops a process.

## How much this is worth, honestly

`Plausible` as a data-collection reduction, on the editions where it does anything beyond
Required. Nothing at all as a performance tweak - the collector is idle-priority and batched, and
the framerate claim attached to it in tweak lists has never been supported by a measurement,
including in this repo.

Filed under **Background & Cleanup**. It belongs to the family of changes that make Windows do
less of what you did not ask for, with no promise attached about games.

## Trade-off

Feedback Hub stops being useful. Some Windows Update servicing decisions use diagnostic data,
and Microsoft's documentation warns that setting the lowest level can affect the delivery of
some updates and features - in practice, rarely noticed on a consumer machine.

Machine-scoped policy: every account on the PC.

## Revert

`nos revert privacy.telemetry-policy` removes the value and returns the machine to whatever the
edition default and Settings toggle say.
