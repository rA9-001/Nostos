## What this changes

<!-- One or two sentences. -->

## Why

<!-- Especially for a tweak: what is the mechanism, and how does it reach a game? -->

---

### Adding a tweak

- [ ] `docs/tweaks/<id>.md` exists, states the group, the category and the evidence rating
- [ ] The page has a **Trade-off** section that names something real
- [ ] The evidence rating is `Measured` only if something was actually measured
- [ ] It is filed under a Gaming category only if the mechanism reaches the game

CI checks the first, third and fourth of those. It cannot check the second, which is the one
that matters most.

### Anything that touches the machine

- [ ] Capture happens before the change, so `nos revert` can undo it
- [ ] Revert restores the **prior value**, including "the value did not exist"
- [ ] `dotnet test Nostos.slnx` passes

### Not accepted

- Kernel drivers, DLL injection, API hooking, or reading another process's memory
- Widening the control pipe's DACL to a broad group
- Making the app elevate itself
- Anything that undoes a user's change without being asked
