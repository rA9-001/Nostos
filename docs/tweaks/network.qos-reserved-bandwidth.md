# network.qos-reserved-bandwidth

**Group:** Gaming · **Improves:** Ping · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.
## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\Psched`
`NonBestEffortLimit` (REG_DWORD) -> `0`

## The claim, and why it is wrong

This is the most copied registry tweak in the history of Windows. The claim goes: *Windows
reserves 20% of your bandwidth for QoS, and this gives it back.*

It does not, because the 20% was never taken away.

`NonBestEffortLimit` is the **ceiling** on how much bandwidth applications may reserve through
the QoS Packet Scheduler, if any application asks. The default of 20 means "no more than 20% of
the link may be reserved by programs that request a reservation". It caps a request; it is not a
standing allocation. If nothing asks - and on a normal desktop nothing does - nothing is
reserved and the whole link is available to ordinary traffic.

Microsoft published exactly this correction more than twenty years ago. It has made no
difference to how often the tweak is recommended.

## So why is it in the catalog?

Two reasons.

1. **People are going to set it anyway.** Every video and every forum thread on the subject says
   to. Setting it here means it is written once, journaled and revertible, rather than pasted
   from a `.reg` file of unknown provenance that also changes eleven other things.

2. **It is genuinely harmless.** Setting the cap to `0` means no program can reserve bandwidth
   through PSched. On a machine where nothing was reserving any, nothing changes.

The **Ping** promise says a tweak here cuts round-trip latency or stops Windows adding delay of
its own. On the overwhelming majority of machines this one does neither, because there was no
delay to remove. This is the clearest example of an unproven tweak anywhere in the catalog.

## When it is not a no-op

If you actually run software that reserves bandwidth through PSched - some enterprise VoIP and
conferencing clients do - then the cap is live, and setting it to `0` stops those reservations
working. Real, if rare.

## Trade-off

None on a normal desktop, because there is no effect on a normal desktop.

## Revert

`nos revert network.qos-reserved-bandwidth` restores the previous value, removing it if the
policy key never had one - which is the usual case. **Machine-scoped**, needs elevation.
