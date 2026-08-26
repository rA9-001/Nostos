# power.throttling-off

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling`
`PowerThrottlingOff` (REG_DWORD) → `1`

The key does not exist on a default install; this tweak creates it, and revert deletes it again
rather than leaving a `0` behind.

## Mechanism

Power Throttling — the user-facing name for EcoQoS — is the Windows feature that decides some
processes are background work and runs them at reduced clocks, preferring efficiency cores on
hybrid CPUs. It is why a minimised or idle-looking process can suddenly be much slower than the
same process in the foreground.

Windows infers "background" from foreground state and window visibility. That inference is
usually right and occasionally very wrong: a dedicated server, an encoder, a launcher doing
shader compilation, or a game running behind a fullscreen overlay can all be throttled while
you are actively waiting on them.

Setting `PowerThrottlingOff` to `1` disables the whole mechanism machine-wide.

## Relationship to `process.game-tuning`

`process.game-tuning` opts **one named process** out of EcoQoS, at the moment you ask it to.
This tweak turns the feature off for **everything**, permanently. They do not conflict, but if
you have applied this one, the QoS half of `process.game-tuning` has nothing left to do.

Prefer the per-process tweak if you only care about one program. Prefer this one on a desktop
where you would rather Windows never made the judgement at all.

## Why "Plausible" and not "Measured"

The mechanism is documented by Microsoft and the setting is theirs. What is not
established is how often Windows throttles something you actually cared about — that depends
entirely on your CPU, your workload and your window arrangement. On a machine where nothing
important is ever misclassified, this changes nothing.

## Trade-off

**It is disabled on battery-powered machines.** The catalog marks it `desktopOnly`, so the
engine refuses to apply it on a laptop that reports a battery, and says so. Turning off the
mechanism whose entire job is saving power costs runtime and adds heat; that is a bad trade on
something you unplug.

On a desktop the cost is that genuinely idle background processes now run at full clocks, which
is a few watts and some fan noise.

## Revert

`nos revert power.throttling-off`. Takes effect for processes started after the change; already
running processes keep whatever throttling state they had.
