# memory.prefetch-tuning

**Group:** Gaming · **Improves:** Performance · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Raises the FPS and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.
## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters`
`EnablePrefetcher` (REG_DWORD) -> `3`, `2` or `0`

**Requires a reboot.**

## Mechanism

The Prefetcher records which pages are read during the first ten seconds of a process's life,
and during boot, and writes a trace into `C:\Windows\Prefetch`. Next time it reads those pages
in one large sequential batch before they are asked for, instead of letting them arrive as a
storm of small random reads.

On a mechanical disk that is a large win, because sequential reads are perhaps a hundred times
faster than random ones. On an NVMe SSD the gap is much smaller and the prefetch is cheap enough
that it is hard to notice either way.

## Options

`--set mode=<option>`, or the radio buttons in the app.

| Option | Value | What it means |
| --- | --- | --- |
| `windows-default` | `3` | **Recommended.** Boot and application prefetch, as Windows ships. |
| `boot-only` | `2` | Keeps boot prefetch, drops the per-application traces. |
| `off` | `0` | No prefetching. Application launches, including the game's, get slower. |

## Why the recommended option is the Windows default

Because this entry exists mainly so that the setting is **pinned and watched**, not so that it
is changed.

"Disable prefetch and superfetch on an SSD" is one of the most durable pieces of bad advice in
PC gaming. It comes from a real early-SSD concern about write amplification that stopped being
true around 2012, and it is still in every tweak pack. Several of those packs set this value to
`0` without telling you.

Writing `3` explicitly means the value is journaled, and means **drift reconciliation will tell
you when something changes it behind your back.** That is worth more than the tweak itself.

Filed under **Performance** because the argument on both sides is about background disk
I/O and launch-time hitching, not about average framerate.

## How much this is worth, honestly

The `off` option is unproven in the strict sense: repeated everywhere, mechanism understood,
benefit unproven, and the cost - slower cold launches - routinely left out. Rating the entry
honestly means rating it by the option people actually come here to set.

## Trade-off

At `off`, every application launch is slower, permanently, including the one you were trying to
speed up. At `boot-only` the cost is limited to application launches. At `3` there is no
trade-off, because nothing has changed.

## Revert

`nos revert memory.prefetch-tuning` restores the previous value and takes effect at the next
boot. **Machine-scoped**, needs elevation.
