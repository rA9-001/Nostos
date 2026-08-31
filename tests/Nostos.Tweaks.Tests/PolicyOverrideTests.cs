using Microsoft.Win32;
using Nostos.Core.Abstractions;
using Nostos.Tweaks.Declarative;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// A tweak whose setting a Group Policy has taken over must not offer to apply.
///
/// Found in the field, on "Remove the Widgets board from the taskbar". Widgets was disabled
/// machine-wide by policy, and Windows then refuses every write to `TaskbarDa`, the per-user
/// value the tweak sets. Not by ACL: the key is fully writable, and creating any other value in
/// the same key succeeds — it is a kernel callback refusing that one value name. So nothing the
/// program could read told it in advance, the row looked available, applying it failed, and the
/// engine rolled back and reported "Did not work".
///
/// Every tweak the catalog offers has to be one the machine will actually accept. The policy is
/// declared on the tweak and checked when applicability is asked, which is also why it is
/// checked every time rather than cached: a policy can arrive or be lifted between two launches.
/// </summary>
public sealed class PolicyOverrideTests : IDisposable
{
    // A key of this test's own, deleted afterwards. The real policies live under
    // HKLM\SOFTWARE\Policies and are not something a test may write.
    private const string TestKey = @"Software\Nostos.Tests\PolicyOverride";

    public void Dispose()
    {
        using var software = Registry.CurrentUser.OpenSubKey(@"Software\Nostos.Tests", writable: true);
        software?.DeleteSubKeyTree("PolicyOverride", throwOnMissingSubKey: false);
    }

    private static void SetPolicy(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(TestKey, writable: true)!;
        key.SetValue("Governed", value, RegistryValueKind.DWord);
    }

    private static void ClearPolicy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TestKey, writable: true);
        key?.DeleteValue("Governed", throwOnMissingValue: false);
    }

    private static ITweak Tweak(string? whenValue)
    {
        var definition = RegistryTweakCatalog.Parse($$"""
            [
              {
                "id": "test.governed",
                "title": "Governed",
                "summary": "A setting a policy can take over.",
                "category": "interruptions",
                "risk": "Safe",
                "evidence": "Plausible",
                "overriddenBy": {
                  "hive": "HKCU",
                  "key": "{{TestKey.Replace(@"\", @"\\")}}",
                  "name": "Governed",
                  {{(whenValue is null ? "" : $"\"whenValue\": \"{whenValue}\",")}}
                  "describe": "Some Policy > Some Setting"
                },
                "values": [
                  { "hive": "HKCU", "key": "Software\\Nostos.Tests", "name": "V", "kind": "DWord", "value": "1" }
                ]
              }
            ]
            """).Single();

        return new RegistryTweak(definition);
    }

    private static async Task<Applicability> Ask(ITweak tweak)
        => await tweak.CheckApplicabilityAsync(new TweakContext());

    [Fact]
    public async Task A_tweak_with_no_policy_declared_is_unaffected()
    {
        var tweak = RegistryTweakCatalog.Parse("""
            [
              {
                "id": "test.plain",
                "title": "Plain",
                "summary": "No policy owns this.",
                "category": "interruptions",
                "risk": "Safe",
                "evidence": "Plausible",
                "values": [
                  { "hive": "HKCU", "key": "Software\\Nostos.Tests", "name": "V", "kind": "DWord", "value": "1" }
                ]
              }
            ]
            """).Single();

        Assert.Null(tweak.OverriddenBy);
        Assert.True((await Ask(new RegistryTweak(tweak))).IsApplicable);
    }

    [Fact]
    public async Task It_is_applicable_while_the_policy_is_absent()
    {
        ClearPolicy();

        Assert.True((await Ask(Tweak(whenValue: "0"))).IsApplicable);
    }

    [Fact]
    public async Task It_is_not_applicable_once_the_policy_is_in_force()
    {
        SetPolicy(0);

        var applicability = await Ask(Tweak(whenValue: "0"));

        Assert.False(applicability.IsApplicable);
        Assert.Contains("Group Policy", applicability.Reason);

        // The reason names where to look, so somebody who wants the tweak can go and lift the
        // policy rather than concluding the program is broken.
        Assert.Contains("Some Policy > Some Setting", applicability.Reason);
    }

    [Fact]
    public async Task The_reason_carries_a_key_so_it_can_be_read_in_German()
    {
        SetPolicy(0);

        var applicability = await Ask(Tweak(whenValue: "0"));

        Assert.Equal("notapplicable.policy", applicability.ReasonKey);
        Assert.Equal(["Some Policy > Some Setting"], applicability.ReasonArgs);
    }

    [Fact]
    public async Task A_policy_set_to_something_else_does_not_take_the_setting_over()
    {
        // The usual shape is one DWORD where 0 disables the feature and 1 explicitly allows it.
        // Only one of the two takes the setting out of the user's hands, and treating "the
        // value exists" as "the policy is in force" would hide a tweak that works perfectly.
        SetPolicy(1);

        Assert.True((await Ask(Tweak(whenValue: "0"))).IsApplicable);
    }

    [Fact]
    public async Task Without_whenValue_the_policy_existing_at_all_is_enough()
    {
        SetPolicy(1);

        Assert.False((await Ask(Tweak(whenValue: null))).IsApplicable);
    }
}
