# memory.paging-executive

**Group:** Gaming · **Improves:** Performance · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.
## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`
`DisablePagingExecutive` (REG_DWORD) -> `1`

**Requires a reboot.** The value is read once, by the memory manager, during initialisation.

## Mechanism

Parts of the kernel and of loaded drivers are marked pageable. Under memory pressure Windows can
write them to the page file and reclaim the RAM, faulting them back in when something calls into
them.

Faulting kernel code back in from disk is a **synchronous stall inside the kernel**. Whatever
thread touched that code waits for a disk read to finish. On a machine that is short of RAM and
paging heavily this is a genuine source of hitching, and it is a hitch that profilers habitually
attribute to the wrong place.

`DisablePagingExecutive = 1` tells the memory manager to keep it all resident. The stall cannot
happen, because the code is never gone.

## How much this is worth, honestly

Because the interesting part of the claim is the part nobody checks: **how often does this
actually happen on your machine?**

On a PC with 16 GB or 32 GB playing a game that fits comfortably, the kernel is never paged out
in the first place, so pinning it prevents a stall that was not going to occur. You have spent
RAM to prevent nothing.

On a PC with 8 GB running a modern title it can matter - but a machine in that state is paging
so much else that the kernel is a small part of its problem.

Old tweak, repeated everywhere, real mechanism, narrow benefit, and the cases where it helps are
not the cases it is usually recommended for. Widely repeated, real mechanism, unproven effect.

## Trade-off

Some tens of megabytes of RAM that Windows can no longer reclaim under pressure. On a machine
with plenty, irrelevant. On a machine that is already short - the machine most likely to benefit
- it makes the shortage slightly worse. The tweak is at its least useful exactly where it is
most often recommended.

## Revert

`nos revert memory.paging-executive` restores the previous value, deleting it if it was not
there. **Takes effect at the next boot**, like the apply.

**Machine-scoped**, needs elevation. Because it needs a reboot, a System Restore point is taken
before it is applied - but nothing rolls the change back on its own, so if the machine misbehaves
after the reboot, revert it yourself.
