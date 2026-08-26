# update.delivery-optimization-off

**Group:** Gaming · **Improves:** Ping · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Cuts round-trip network latency, or stops Windows adding delay of its own to traffic the game is waiting on.
## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization`
`DODownloadMode` (REG_DWORD) -> `0` or `1`

The same setting as Settings, Windows Update, Advanced options, Delivery Optimization.

## Mechanism

Delivery Optimization is peer-to-peer distribution for Windows updates and Store apps. Your PC
downloads pieces from other machines, and hands pieces back out to them.

The download half is fine and often faster. **The upload half is the problem.** Uploading
saturates the upstream side of your connection, and a saturated uplink is what a ping spike
actually is: your ACKs and your input packets queue behind bulk data in a router buffer. This is
bufferbloat, it is well understood, and it does not care that the bulk data happens to be a
Windows update.

That is why this is filed under **Ping** and not under Ping. The mechanism is bandwidth
contention and the symptom is latency that spikes for a few minutes at a time for no reason you
can see.

## Options

`--set mode=<option>`, or the radio buttons in the app.

| Option | `DODownloadMode` | What it means |
| --- | --- | --- |
| `off` | `0` | **Default.** HTTP only, straight from Microsoft. This PC never uploads. |
| `lan-only` | `1` | Peers on your own network only. Nothing leaves the router. Genuinely useful with several Windows PCs at home. |

## Why "Plausible" and not "Measured"

The bufferbloat mechanism is solid and easy to demonstrate with any upload. What is not measured
here is how much any given machine actually uploads, which depends on how many peers are nearby
and what Microsoft is distributing that week. It can be zero for months and then very noticeable
on patch Tuesday.

For the real answer on your machine: Settings, Windows Update, Advanced options, Delivery
Optimization, Activity monitor shows exactly how much has been uploaded.

## Trade-off

Update downloads may be slower on a slow connection, since they can no longer be pulled from a
neighbour. That is a cost paid once per update against a benefit paid every evening.

## Revert

`nos revert update.delivery-optimization-off` restores the previous value, removing it if the
policy was never set. **Machine-scoped**, needs elevation.

Related: [`services.delivery-optimization`](services.delivery-optimization.md) stops the service
that does the work. Either is enough on its own. The policy is the gentler of the two, since it
leaves the service in place for anything else that wants it.
