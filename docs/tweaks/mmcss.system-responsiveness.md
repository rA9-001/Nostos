# mmcss.system-responsiveness

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile`
`SystemResponsiveness` (REG_DWORD)

Windows client ships this at `20`. **You choose the value** — this tweak offers three, and the
app shows the trade-off next to each one.

## Options

`--set reserve=<option>`, or the radio buttons in the app's detail pane.

| Option | Value | What it means |
| --- | --- | --- |
| `windows-default` | 20 | The shipped value, written explicitly. Useful for pinning it so drift reconciliation catches a Windows update that changes the default. |
| `balanced` | 10 | **Default and recommended.** Halves the reservation. Leaves enough for the audio engine while giving MMCSS-registered game threads more room. |
| `none` | 0 | Reserves nothing for non-multimedia work. See below. |

`nos show mmcss.system-responsiveness` prints the same thing with the full descriptions.

## Mechanism

`SystemResponsiveness` is the percentage of CPU the Multimedia Class Scheduler Service
guarantees to *non*-multimedia work. Lowering it leaves more headroom for threads registered
with MMCSS. It is a documented, supported setting.

## Why "Plausible" and not "Measured"

The mechanism is real and documented, but the effect on a game depends entirely on whether
that game's threads register with MMCSS. No frametime data has been collected for this repo
yet. If you measure it, open a PR with the numbers and this rating goes up.

## About the `none` option

Zero is widely recommended on forums and it starves the audio engine, which shows up as
crackling or dropouts under load. It is **not** the default here and it is not recommended.

It is offered anyway, for the same reason `gpu.tdr-delay` is in the catalog at all: people set
it regardless, and setting it here means the prior value is captured, the change is journaled,
and `nos revert` puts it back. The alternative is that they set it by hand in regedit with no
record of what it was before.

If you stream, encode, or run anything else while playing, use `balanced` or
`windows-default`.

## Revert

`nos revert mmcss.system-responsiveness` restores the exact prior value, including deleting
the value entirely if it did not exist before.
