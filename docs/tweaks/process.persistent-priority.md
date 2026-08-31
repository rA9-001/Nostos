# process.persistent-priority

**Group:** Gaming · **Improves:** Performance · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Raises the FPS and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

For one executable, by name:

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options
    \<game>.exe\PerfOptions
        CpuPriorityClass = 6   (Above normal, the default here)
```

Windows reads that value in the loader, every time an image with that file name starts. Nothing
has to be running to arrange it, and nothing has to be re-done after a reboot.

## Why this exists alongside `process.game-tuning`

[`process.game-tuning`](process.game-tuning.md) raises the priority of a process that is already
running. That is the safer mechanism and the one that can be undone mid-match, but it has to be
done again after every launch — and in practice nobody opens a window before each session to do
it, which makes a real tweak into a chore nobody performs.

This one is the other half. Between them:

| | `game-tuning` | `persistent-priority` |
|---|---|---|
| Applies to | one running process | every launch of an image name |
| Survives restart | no | yes |
| Takes effect | immediately | next time the game starts |
| Also sets EcoQoS | yes | no — there is no loader equivalent |
| Risk | Safe | Moderate |

Using both is reasonable: this one for every future launch, `game-tuning` for the session already
in progress.

## Why Moderate and not Safe

`game-tuning` is Safe because its worst case is a process exiting. This one is not, for three
reasons worth reading before applying it:

- **It is machine-wide and matched on the bare file name.** Every `cs2.exe` on the system gets
  it, from any folder, for every user.
- **It survives reboots**, and applies before anything — including Nostos — is in a position to
  intervene.
- **It is keyed under Image File Execution Options**, the same key whose `Debugger` value is a
  well-known persistence trick. Nothing here reads, writes or removes `Debugger`, and the
  cleanup on revert deletes a key only when it is completely empty, so an entry another tool
  owns is left alone. But security software does watch this key, and a warning about it is not
  a false positive in the sense of being wrong about what was written.

## Only Above normal and High

The registry accepts Idle, Below normal and Normal here as well, and none of them are offered.
A permanent setting that makes a game slower on every launch is not something the catalog should
be able to do by accident.

Realtime is refused for the same reason [`process.game-tuning`](process.game-tuning.md) refuses
it, and more strongly: realtime outranks input, audio and the mouse cursor, and a permanent one
would apply before anything was running that could take it back.

**Above normal** is the recommendation here, where **High** is the recommendation for
`game-tuning`. The difference is the permanence — a game that hangs at High is harder to click
away from than one at Above normal, and this setting arranges that on every launch rather than
on the one you asked for.

## Revert removes every game, not just the selected one

This is the one place this tweak departs from the usual shape, and it is worth knowing before
you set up three games and then change your mind about one.

The journal keeps one snapshot per tweak id — the oldest, so that applying something twice still
reverts to the machine as it was originally. This tweak is applied once per game. If its snapshot
held only the game being set, then setting `cs2` and later `valorant` would leave the second with
no record and no way back: permanent, machine-wide and invisible.

So the snapshot is **every permanent priority on the machine**, captured at each apply. Revert
restores exactly that map — which for most machines is the empty set — however many games were
set in between. To remove one game and keep the others, revert and set the others again, or
delete that one key by hand.

## Usage

### In the window

Select the tweak and pick the game from **Target process**, above the Apply button. The picker
lists running programs, and the tweak takes the executable's name from the one you choose — so
you point it at the game once, while it happens to be running, and every launch after that
starts at the priority you picked.

### On the command line

`--set exe=` names an executable directly, which is the case the picker cannot cover: a game
that is not running yet.

```
nos apply process.persistent-priority --set exe=cs2.exe
nos apply process.persistent-priority --set exe=cs2.exe --set priority=High
nos apply process.persistent-priority --process cs2
nos revert process.persistent-priority
```

`--process` resolves a running process to its image name, exactly as the picker does. A full path
is accepted and reduced to its file name, since that is all the loader matches on.

## Evidence

**Plausible.** The mechanism is documented and the effect is the same one `process.game-tuning`
produces — this changes only *when* the priority is set, not what it does. There are no frametime
measurements for it separate from those, and there would be nothing new in them: a process at
Above normal behaves the same whether the loader set it or a program did a second later.

What is genuinely different is the reliability. A tweak that has to be re-applied by hand every
launch is applied on some fraction of launches, and that fraction is not high.
