# gamedvr.user-capture-off

**Group:** Gaming · **Improves:** Performance · **Risk:** Safe · **Evidence:** Measured · **Scope:** User · **Reboot:** no

> Raises the framerate and evens out frametimes, by giving the game CPU, GPU or memory that Windows was spending on something else.

## What it changes

`HKCU\System\GameConfigStore`
`GameDVR_Enabled` (REG_DWORD) → `0`

## Note on scope

This is a per-user key. When the optimizer service applies it, it must impersonate the signed-in
user first — a service running as `LocalSystem` writing to `HKCU` writes to `SYSTEM`'s own hive,
which achieves nothing. The CLI, running as the user, has no such problem.

See [architecture.md](../architecture.md#user-scoped-tweaks) for how the service handles this.

## Revert

`nos revert gamedvr.user-capture-off`.
