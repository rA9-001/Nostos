# The Apps tab

Everything installed on this PC, in one list, with a Remove button on each row.

## What it lists

Six sources, merged and de-duplicated:

| Source | Where it is read from |
|---|---|
| Machine-wide Win32 | `HKLM\…\CurrentVersion\Uninstall` |
| Machine-wide, 32-bit | the same key as a 32-bit program sees it |
| Per-user | `HKCU\…\CurrentVersion\Uninstall` — Discord, most launchers |
| Microsoft Store / MSIX | the AppModel package repository |
| scoop | `~\scoop\apps` |
| Chocolatey | `%ProgramData%\chocolatey\lib` |

On the machine this was built on that is **106 programs from about 300 registry keys** — the
difference being Windows' own rules for what counts as a program rather than a patch record, a
system component or a shared runtime.

### What it cannot show

**A program that was never installed.** A game unzipped into `D:\Games` and run from there
registers nothing, has no uninstaller and no package. There is nothing to enumerate. The page
says so at the bottom rather than quietly implying it is complete.

## Icons

Every row carries a picture. Five sources are tried in turn, each a weaker claim about what the
program looks like than the one above it, and the first hit wins:

1. **The icon the publisher nominated** in the registry (`DisplayIcon`). Checked against the disk
   rather than trusted, because the shell answers for a file that is not there: ask it about a
   deleted path and it hands back the blank-page icon for the extension, which is worse than
   nothing since it is not null and so ends the search.
2. **The icon Windows Installer cached at install time.** An MSI carries its artwork as a
   property inside the package rather than as a file, so an MSI-installed program routinely has
   no `DisplayIcon`, no `InstallLocation` and nothing on disk with its name on it -- Epic Games
   Launcher has all three absent. What it has is an entry in the installer's own product
   database pointing at `%WINDIR%\Installer\{ProductCode}\`, which is where Programs and Features
   gets the picture it shows.

   The awkward part is the key name. Products are filed under a **packed** product code -- the
   GUID with its first three fields reversed and the rest byte-swapped, so
   `{D5B1B157-0F47-4E06-9C9A-97BD8A3E6039}` is stored as `751B1B5D74F060E4C9A979DBA8E30693`. It
   is a fixed transform rather than a hash, so it is computed rather than searched for; getting
   it wrong produces a key that does not exist, which is indistinguishable from a product having
   no icon.

   These files are also frequently **extensionless** -- `ProductICO`, `ARPIco` -- and the shell
   identifies files by extension, so it answers for those with a blank page. They are read
   straight out of the file's resources instead.
3. **For a packaged app, the artwork inside the package.** This needs its own step because a
   Store app's launcher under `WindowsApps` is a zero-length reparse point the shell cannot read,
   so there is no icon to ask Windows for. The logo is an ordinary PNG, usually in a **sibling**
   package ending `_split.scale-100` rather than the one the registry names, under `Assets\` --
   or `Images\`, which is not the convention but is what MSI Center uses. File names are the
   author's choice, so the search ranks by role: the app-list icon, then the 44px tile, then the
   Store logo, and never a wide tile, which is a banner rather than an icon.

   The sibling's name is **constructed rather than looked up**, and that detail is load-bearing.
   Listing `C:\Program Files\WindowsApps` requires administrator rights, while reading inside a
   package whose name you already know does not -- so a search that finds siblings by listing
   that folder works perfectly when elevated and blanks every Store icon for everybody else.
   A split package shares its parent's identity, version and publisher hash and differs only in
   the scale, and Windows defines the set of scales, so each candidate can be written down and
   checked with an existence test instead. The starting point is the location the package
   repository recorded, not an assumed path, so an app installed to another drive is found as
   well.
4. **The program's own uninstaller**, which is usually branded even when nothing else is. Not one
   that belongs to Windows, though: the rule is where the file lives rather than a list of names,
   so anything inside the Windows directory is refused and msiexec, rundll32, cmd and powershell
   are all covered at once. Accepting `msiexec.exe` here would end the search early and draw a
   package that has a perfectly good executable in its install directory as the Windows Installer
   instead of as itself.
5. **The largest executable in the install directory**, which is what the shell would have drawn
   for the program's shortcut. By size, so a 400 KB launcher wins over a 12 KB helper that sorts
   first: crash handlers and updaters live in these folders too and rarely carry the artwork.

That finds the program's own artwork for **94 of the 105 programs** on the machine this was built
on. The other eleven -- seven Visual C++ runtimes, a couple of SDK components -- are not hiding
it; they ship none, anywhere. There is no `DisplayIcon`, no cached product icon, no install
directory and no Start menu shortcut, and Programs and Features draws them as a generic installer
package because that is all there is to draw.

So Nostos draws the same thing: **the icon Windows uses for an installer package**, taken from the
shell's answer for the `.msi` file type. It says the one true thing left to say about the row.
Eleven rows sharing a picture is a fair trade for no row being blank, and "this is a runtime, not
an application" is a useful thing for a list like this to communicate.

Anything that is neither a package nor has artwork falls back to a **monogram** -- one or two
letters on a tile coloured by a hash of the name, so it is the same colour every launch and can
be found by shape. No row on this machine reaches it, but there is no picture of "a program in
general" worth drawing, and a wrong icon is worse than letters when the icon is what somebody
scans the list by.

## Why is this even listed?

Every row has a **Details** button, and it answers exactly that.

The question comes up because of what the list actually is. A row exists because something on the
machine registered it — almost always a key under `…\CurrentVersion\Uninstall`. That key is not
the program. An uninstaller that deletes a program's files and does not delete its own key leaves
an entry Windows still believes in, so the row survives the program by months. Teamfight Tactics
on the machine this was written on is exactly that: the game is gone, its key is not.

Without an explanation the only thing the window can say is that the program is installed. To
somebody who knows it is not, that reads as the list being wrong rather than the registry being
stale, and a list you have caught lying once is not one you will act on again.

So the panel shows three things.

**What put the row here**, written out in full and selectable, because the useful thing to do with
a registry key you do not believe is to paste it into regedit and look:

```
Registered at   HKEY_CURRENT_USER\SOFTWARE\…\Uninstall\Riot Game teamfighttactics.live
Uninstaller     C:\Riot Games\Riot Client\RiotClientServices.exe          still there
Icon            C:\ProgramData\Riot Games\Metadata\…\teamfighttactics.ico gone
```

**Whether it is still true.** Each path the entry records is checked, and the summary says which
of three cases this is: everything present, nothing present, or somewhere in between. The middle
case is a real shape rather than an evasion — a game whose files were deleted while its launcher
stayed is neither installed nor a leftover, and saying "1 of the 2 paths this entry records is
missing" is more use than a verdict that has to pick one.

An uninstall command with no path in it — `MsiExec.exe /X{…}`, the commonest one on any machine —
is resolved the way Windows resolves it, against the search path. Checking the bare name would
resolve it against the working directory instead and report fifteen rows here as having a missing
uninstaller, every one of them wrong, in the exact place somebody came to find out whether a row
can be trusted.

**What it owns**, from the same scan the sweep uses, with sizes and with anything that looks like
your own files marked. An icon the entry records is included when it sits outside the folders
already listed: a stale entry often points at one under the publisher's folder rather than the
program's -- Riot keeps a game's icon in `ProgramData\Riot Games\Metadata\...` -- and the
leftover scan cannot propose that, because it matches on the program's name and refuses
substrings. It is a real leftover that nothing else here would offer to clear up.

### Removing it, all of it or one piece of it

Under the explanation is the list of everything the entry owns -- each folder and registry key
found under its name, an icon file it records if that sits outside them, and the entry itself.
**Each one has its own Remove button**, and there is one button for all of them together.

Both are worth having. "The registry entry is stale but I want to keep the folder" and "clear the
lot" are both ordinary things to want, and a tool that only offers the second makes somebody open
regedit to get the first.

The list of things a program owns is **rebuilt from the machine before any single removal
happens, and the path has to be on it.** That check is the security boundary rather than a
nicety: removal takes an app id *and* a path, and without the pairing it would be a way to
recycle any folder on the machine, or export and delete any registry key, by naming a program
alongside it. Windows' own directories are refused by that check like anything else the program
does not own, before the protected-path list is even consulted.

The uninstaller is deliberately never offered. Deleting it strands a program that is still
installed with no supported way to remove it, which is a worse position than the one somebody
opened the panel to get out of.

The button for all of it removes the registration **and** everything listed, without running an
uninstaller.

That last part is the point of it. `Remove` runs the program's own uninstaller and then refuses to
sweep if the program is still registered afterwards — which is right when the program is real, and
a dead end when it is not: there is no uninstaller to run, or there is one that shrugs because its
program is already gone, and either way the entry is still there at the end. These are two
different claims about the machine and they are two different buttons. One says "uninstall this";
the other says "the program is gone, take its entry away as well". A single button that quietly
did whichever seemed to apply would be guessing on the reader's behalf about the one thing they
are there to decide.

It is the same reversible machinery as everything else here: the key is exported to a `.reg` file
in `%ProgramData%\Nostos\removed\` before it is removed, folders go to the Recycle Bin, and a
failure is never turned into a delete. `reg import` puts the entry back and the row returns.

Two rules keep it honest:

- **Only a registry entry can be forgotten.** A Store package, a scoop directory or a Chocolatey
  one is not a note about a program, it is the program, so the button is not offered — removing
  the registration there would hide something that is still installed, which is the opposite of
  what this list is for.
- **The registration goes last.** A sweep that fails half way leaves the row in the list with the
  rest of it still findable. Removing the entry first would hide whatever was left behind, which
  is the one outcome worse than doing nothing.

The entry is also filtered out of its own leftover list. The scan finds it — it is a registry key
under the program's name, which is what that scan looks for — and showing it in both places would
read as two things to deal with when it is one.

**What needs administrator rights is marked before the button is pressed.** An HKLM key, or a
folder under Program Files, ProgramData or Windows, is out of reach of a copy that was not
started elevated, and the panel knows that at the moment it reads the machine: it is decided from
the path, in the process that would do the removing. So the row carries a *Needs administrator*
note, the Forget button carries the sentence saying what would make it work, and neither is
hidden or disabled. The alternative is what was there before — pressing Remove and getting
reg.exe's own answer, which says access is denied without saying whose.

The marking is deliberately narrow. A path the account owns is never claimed to need rights, even
where it might fail for some other reason: claiming them when they are not needed teaches
somebody to restart the program elevated out of habit, which is the habit this program is built
not to ask for.

## Removing something

Two halves, and they are very different in character.

**First, the program's own uninstaller runs** — the same one Windows would run, doing whatever
its author decided. Where a silent removal is genuinely available it is used: an MSIX package,
a scoop or Chocolatey package, an installer that supplied its own `QuietUninstallString`, or an
MSI, where `MsiExec.exe /I{GUID}` can be rewritten to `/X{GUID} /qn /norestart`. That last one is
documented Windows Installer behaviour, not a guess.

Where it is not available, the uninstaller runs visibly and asks its questions. **The row says
which of the two it will be before you press the button** — "unattended" or "asks questions" —
because a wizard opening over the window is a worse surprise than a row that warned you.

Nostos never elevates itself. An uninstaller needing administrator rights raises its own UAC
prompt, exactly as it would from Programs and Features.

**Then the leftovers are swept up.** Folders and registry keys that still bear the program's
name, after its uninstaller has had its turn.

## The part worth arguing about

Finding leftovers is **name matching, and name matching is guesswork.** The only way to know for
certain what an installer created is to have watched it install, which needs a filesystem filter
driver — a kernel driver, which this program will not ship.

Every tool in this space does the same guessing and then *deletes* what it found. That is where
their worst outcomes come from, and the example is not hypothetical: a great many games keep
their save files in `%LOCALAPPDATA%\<GameName>`, which is a leftover by every rule you could
write. "Thorough" and "deleted my saves" end up being the same code path.

So three things are true here instead:

### Nothing is deleted

- **Folders go to the Recycle Bin**, via `SHFileOperation` with `FOF_ALLOWUNDO`. The undo is
  performed by Windows, in a place people already know how to look, and it outlives Nostos.
- **Registry keys are exported to a `.reg` file first**, into `%ProgramData%\Nostos\removed\`, and
  only then removed. `reg import` puts one back.
- **A failure is never turned into a delete.** If the Recycle Bin cannot take something, or a key
  cannot be backed up, the item is left exactly where it is and the reason appears on the row.
  Doing the irreversible thing at the moment the reversible one turned out to be unavailable is
  precisely backwards.

### The guessing is as narrow as it can be

- A candidate must match the program's name **in full**, never as a substring. Substring matching
  is how uninstalling "Microsoft Teams" proposes deleting `%LOCALAPPDATA%\Microsoft`, taking
  Edge, OneDrive and the Windows shell's own state with it.
- Only a **fixed set of parent directories** is looked in, plus the program's own recorded install
  location. The scan never walks the disk, so it cannot wander into a folder that merely shares a
  word with the program.
- The registry side is a fixed list too, deliberately **not** a search of `HKCU\Software` for
  anything containing the name. The publisher's own key is never a candidate on its own — it
  usually holds settings for their other programs.
- A name shorter than three characters produces no candidates at all.
- Some places are refused whatever the match says: a drive root, Windows itself and anything
  inside it, `Program Files`, `ProgramData`, the user profile, and `Program Files\WindowsApps`.

**Only Win32 installers are scanned.** MSIX packages are self-contained by design, so a scan over
one finds no true leftovers and two kinds of false one — the package's own directory under
`WindowsApps`, which the deployment stack owns, and system keys that merely share the package's
name. Both were found by running the scan over a real machine and reading what it proposed: the
Store package called "DirectX" put forward `HKLM\SOFTWARE\Microsoft\DirectX`, which is Windows'
own. scoop and Chocolatey own their directories the same way.

### The exit code is not the evidence

This is the check the first draft did not have, and it is the most important one in the feature.

A Steam game's uninstaller is `steam.exe steam://uninstall/960090`. That process hands the request
to the Steam already running and **exits in about a second with code zero** — long before Steam
has drawn the dialog asking whether you meant it. A sweep that trusted the exit code would move
118 GB of Elden Ring to the Recycle Bin while the user was still deciding, and would do the same
after they pressed Cancel. Epic and several other launchers hand off the same way.

So the exit code is not what the sweep is conditioned on. **The machine is.** After the
uninstaller returns, Nostos re-enumerates: if the program is still registered, it is still
installed, nothing is swept, and the row says so and suggests finishing in the launcher.

## It is in the History tab

Every removal is recorded, with what the sweep did:

```
✓  Uninstalled — Contoso Widget
   Contoso Widget — uninstalled, 3 folder(s) to the Recycle Bin,
   2 registry key(s) backed up and removed
```

**Committed with no preceding intent**, exactly like a startup switch and for the same reason:
the outstanding set `nos revert --all` works from is built out of intents carrying a snapshot, so
a committed-only line is visible in the history and is never something revert goes looking for.
Reverting an unrelated tweak must not try to reinstall Discord.

That is not the same as the removal being unrecoverable. The undo exists — it is performed by the
Recycle Bin and by `reg import`, in the places people already look, rather than by a tweak engine
that has no way to reinstall a program.

A removal is recorded **only when the program actually went.** A cancelled uninstall leaves the
machine as it was, and a line saying otherwise would be the one kind of lie this log must not
contain.

## Known limits

- **Removing an HKLM leftover key needs elevation.** The uninstaller prompts for its own UAC, but
  the sweep runs in the app, so a machine-wide registry leftover is out of reach unless Nostos
  was started elevated. That is now marked on the row before the button is pressed rather than
  reported after it; routing it through the service is still the obvious next step.
- **A very large folder may not fit in the Recycle Bin.** It is left where it is and reported,
  rather than deleted.
- `EstimatedSize` is absent more often than it is present, so the size column is frequently blank.
  A blank cell is honest; "0 MB" would be a claim.
