# cpu.priority-separation

**Group:** Gaming · **Improves:** Performance · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl`
`Win32PrioritySeparation` (REG_DWORD)

Windows client ships `2`. The options write `2`, `38` (0x26), `40` (0x28) or `24` (0x18).

## Mechanism

The value is six meaningful bits, read as three pairs:

| Bits | Meaning | `0` | `1` | `2` |
| --- | --- | --- | --- | --- |
| 5:4 | Quantum length | SKU default | Long | Short |
| 3:2 | Quantum type | SKU default | Variable | Fixed |
| 1:0 | Foreground quantum boost | 1x | 2x | 3x |

*Quantum* is how long a thread runs before the scheduler considers someone else. *Variable*
means the foreground process's threads get a longer one than everything else; *fixed* means
everyone gets the same. The bottom pair sets how much longer, as a multiple of the base.

A clean desktop install writes `2`, which is bits 5:4 and 3:2 both saying "use the SKU default"
- and on client that default is short, variable, 3:1. So the famous `0x26` is not a change at
all: it is the same policy written out in full instead of delegated.

## How much this is worth, honestly

`Plausible`, and only barely. The setting is real, documented in *Windows Internals*, and the
kernel picks up a change without a reboot. What is not real is the claim that `26` or `38`
raises framerates: on a desktop SKU the recommended option is behaviourally identical to the
default, and the two options that *are* different trade in the opposite direction from what the
forums assume.

The reason it is in the catalog is that it is one of the most-copied values in Windows tweaking
and almost nobody who sets it can say what they set. Applying it here writes a value you can
read back, and reverting puts the original number back exactly - including the difference
between "2" and "not present".

If you want the option that might actually change something you can feel, it is
`background-services`, and it will make the machine feel *less* responsive in the foreground in
exchange for smoother behaviour while streaming or encoding.

## Trade-off

`short-fixed` removes the foreground boost, so a background compile or a shader cache build
competes with the game on equal terms. `background-services` goes further and lengthens the
quantum too, which is the right answer for a machine doing two jobs and the wrong one for a
machine playing a game.

## Revert

`nos revert cpu.priority-separation`.
