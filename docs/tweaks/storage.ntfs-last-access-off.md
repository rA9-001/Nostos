# storage.ntfs-last-access-off

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Moderate · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** yes

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`
`NtfsDisableLastAccessUpdate` (REG_DWORD)

Windows ships `0x80000002`. The options write that or `0x80000001`.

## Mechanism

The value is two fields. The high bit says who owns the setting - `0x80000000` set means Windows
manages it and may change it, clear means you do. The low bits say whether last-access
timestamps are written: `0` for on, `1` for off.

So:

| Value | Meaning |
| --- | --- |
| `0x80000000` | System managed, updates on |
| `0x80000001` | User managed, updates off |
| `0x80000002` | System managed, updates on - **the shipped default** |
| `0x80000003` | System managed, updates off |

With updates on, NTFS writes a timestamp into a file's MFT record when the file is *read*. That
is a metadata write caused by an operation that changed nothing. NTFS batches these - it does
not write one per open - but under a workload that touches thousands of files, such as a game
launcher verifying an installation or a shader cache warming up, the batched writes are real.

Choosing `off` also takes the high bit down, which stops Windows deciding to turn the feature
back on later.

## How much this is worth, honestly

`Plausible`. The write is real and removing it is real, but nobody has produced a framerate
number for it and it would be surprising if one existed: the writes land during file access, not
during a frame.

This is a disk-lifetime and background-I/O entry, which is why it is filed under **Background &
Cleanup** and not Performance. `fsutil behavior set disablelastaccess 1` does the same thing;
doing it here means it is journaled and the exact prior value comes back on revert.

## Trade-off

Anything that selects files by when they were last *read* loses its input. That is a short list
but not an empty one: some backup and archiving tools use last-access to find cold data, storage
tiering uses it, and forensic timelines depend on it.

Needs a reboot - the value is read when the volume is mounted.

## Revert

`nos revert storage.ntfs-last-access-off`, then reboot.
