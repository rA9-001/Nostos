# privacy.advertising-id-off

**Group:** Windows · **Improves:** Background & Cleanup · **Risk:** Safe · **Evidence:** Plausible · **Scope:** Machine · **Reboot:** no

> Stops Windows running services and features you do not use. Frees some memory and boot time; does not, on its own, promise you frames.

## What it changes

`HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo`
`DisabledByGroupPolicy` (REG_DWORD) -> `1`

The value does not exist on a clean install.

## Mechanism

Windows issues each user account a stable identifier and exposes it to apps through
`Windows.System.UserProfile.AdvertisingManager`. Apps that show ads use it to correlate what you
do in one app with what you do in another, and to attribute installs.

The policy makes the API return an empty string. It is not a per-app setting and it is not the
per-user toggle in Settings > Privacy - it is the machine-wide policy that overrides it and
greys it out.

## How much this is worth, honestly

`Plausible`, and it is a one-line policy with a one-line effect. There is no performance claim
here at all and the page will not invent one: retrieving an ID costs nothing, and the apps that
would use it are Store apps, which most gaming machines barely run.

It is in the catalog because it is asked for constantly, it is trivially revertible, and doing
it here means the change is recorded rather than being one more line in a script somebody
pasted.

Filed under **Background & Cleanup** with the rest of the entries that tidy Windows up without
claiming to speed it up.

## Trade-off

Store apps that show ads show untargeted ones instead of none. Nothing stops working.

Machine-scoped: it applies to every account, and it disables the per-user toggle in Settings
rather than sitting alongside it.

## Revert

`nos revert privacy.advertising-id-off` removes the value, which returns control of the setting
to the per-user toggle.
