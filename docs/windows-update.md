# The Windows Update tab

Everything this program can do about Windows Update, on one page, with a switch on each row.

## Why it is a tab and not a category

The tweaks on this page sit in **three different categories**, on purpose:

| Tweak | Category |
|---|---|
| `stability.driver-search-off` | Crashes & Freezes |
| `update.active-hours` | Interruptions |
| `update.no-restart-notifications` | Interruptions |
| `update.pin-windows-version` | Interruptions |
| `update.no-auto-restart` | Interruptions |
| `update.notify-before-download` | Interruptions |
| `update.delivery-optimization-off` | Ping |
| `update.store-auto-download-off` | Ping |
| `services.delivery-optimization` | Background Services |
| `services.windows-insider` | Background Services |

That spread is not an accident to be tidied up. A category in this program is **a claim about
what a tweak does for the player** — `nos categories` prints what each one promises — and "which
part of Windows it writes to" is not one of those claims. A driver getting swapped underneath you
is a crash you have not had yet. A restart toast is an interruption. A background download
competing for your uplink is a ping problem, and it is the same ping problem whether the
download came from Windows Update or from the Store.

Adding a `windows-update` category would have made the catalog worse in exactly the way
`background` did before it was split: a bucket named after a mechanism, whose promise has to be
vague enough to cover a GPU driver and a Store download at once.

So the categories stay as they are, and this tab is **a second way in** — for a reader who
arrived thinking "Windows Update keeps interrupting me" rather than thinking "ping".

Membership comes from the `windows-update` tag. Adding a tweak to this page is adding that tag,
and a test asserts that everything whose id begins `update.` carries it.

## The rows are the same rows

A row here is **the same view model object** the Tweaks tab shows — not a copy of it. Two view
models over one tweak is how a page ends up reading ON while the page beside it reads OFF, with
nothing on screen to say which is right.

Clicking a row switches it, the way a Startup row switches a program: apply if it is off,
**revert if it is on**. Revert rather than "apply the opposite", because these have captured
prior values, and putting the captured value back is the only undo that restores what was
actually there rather than what this program believes the default to be.

Risk and evidence chips stay on every row. These are real tweaks with real trade-offs, and a
friendlier-looking page is the wrong place to stop saying so — `update.pin-windows-version` is
rated Moderate and looks it.

## What this page will not do

There is no "disable Windows Update" button, and there will not be one. Every other tool in this
space ships one; here is why this one does not.

The usual recipe sets `NoAutoUpdate=1`, disables `wuauserv`, `BITS` and `UsoSvc`, disables the
UpdateOrchestrator and WaaSMedic scheduled tasks, and **deletes `C:\Windows\SoftwareDistribution`**.

Two of those steps are disqualifying on their own terms, before anyone argues about whether it is
a good idea:

- **The delete cannot be captured**, so it cannot be reverted, so it cannot be an `ITweak` at
  all. Every change this program makes is captured before it is made; a step that has nothing to
  capture breaks that contract outright.
- **WaaSMedic exists to repair update tampering**, on its own schedule. The service half of the
  recipe would be undone by Windows within days, and `VerifyAsync` would report the tweak as
  drifted forever. A row that permanently reports itself broken is worse than an absent one.

What is offered instead is the half that works and stays working: **keep the security patches,
stop the parts that interrupt you.**

## Editions

Two of these are Windows Update for Business policies, which **Windows Home ignores outright**:
`update.pin-windows-version` and `update.store-auto-download-off`. They are marked `proOnly` in
the catalog and report themselves not applicable on Home rather than writing a value that would
sit in the registry doing nothing. `nos doctor` prints the edition.

See [update.pin-windows-version](tweaks/update.pin-windows-version.md) for why that matters more
than it sounds: those policies are the one class of registry value where every observable sign
says the change worked and nothing happened.

## On the command line

The tab is a view, not a mechanism. Everything on it is an ordinary tweak:

```
nos list --category interruptions
nos show update.active-hours
nos apply update.active-hours
nos revert update.pin-windows-version
```
