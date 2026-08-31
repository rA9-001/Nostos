using Nostos.Core.Abstractions;
using Nostos.Tweaks.Declarative;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Windows Update for Business policies, and the edition that ignores them.
///
/// These are the one class of registry value where every observable sign says the change
/// worked and nothing happened. The key under `Policies\Microsoft\Windows\WindowsUpdate` is
/// writable on Home, the value stays where it was put, a read-back returns it, and Verify
/// reports no drift -- the update client on Home simply never looks at it.
///
/// That is worse than a failure. A tweak that fails says so; this one would sit in the list
/// with a tick beside it, and the whole argument for this program is that the list is true.
/// So the edition is checked before the tweak is offered.
/// </summary>
public sealed class ProOnlyTests
{
    private static ITweak Tweak(bool proOnly)
    {
        var definition = RegistryTweakCatalog.Parse($$"""
            [
              {
                "id": "test.wufb",
                "title": "A Windows Update for Business policy",
                "summary": "Something Home would ignore.",
                "category": "interruptions",
                "risk": "Safe",
                "evidence": "Plausible",
                "proOnly": {{(proOnly ? "true" : "false")}},
                "values": [
                  { "hive": "HKLM", "key": "SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "name": "TargetReleaseVersion", "kind": "DWord", "value": "1" }
                ]
              }
            ]
            """).Single();

        return new RegistryTweak(definition);
    }

    private static async Task<Applicability> Ask(ITweak tweak)
        => await tweak.CheckApplicabilityAsync(new TweakContext());

    [Fact]
    public void The_flag_defaults_to_off_so_adding_it_is_a_deliberate_act()
    {
        var definition = RegistryTweakCatalog.Parse("""
            [
              {
                "id": "test.plain",
                "title": "Plain",
                "summary": "Nothing edition-specific about it.",
                "category": "interruptions",
                "risk": "Safe",
                "evidence": "Plausible",
                "values": [
                  { "hive": "HKCU", "key": "Software\\Nostos.Tests", "name": "V", "kind": "DWord", "value": "1" }
                ]
              }
            ]
            """).Single();

        Assert.False(definition.ProOnly);
    }

    [Fact]
    public async Task A_tweak_that_is_not_marked_pro_only_is_offered_on_every_edition()
        => Assert.True((await Ask(Tweak(proOnly: false))).IsApplicable);

    [Fact]
    public async Task A_pro_only_tweak_is_offered_exactly_when_this_is_not_Home()
    {
        // Written against the machine the tests run on rather than a stub, because the thing
        // worth holding is that the gate is wired to the real edition. Both branches are
        // meaningful: on a Home CI runner this asserts the refusal, on Pro it asserts that the
        // gate does not fire on a machine where the policy does work.
        var applicability = await Ask(Tweak(proOnly: true));

        Assert.Equal(!SystemInfo.IsHomeEdition, applicability.IsApplicable);
    }

    [Fact]
    public async Task The_refusal_says_why_rather_than_just_no()
    {
        var applicability = await Ask(Tweak(proOnly: true));

        if (applicability.IsApplicable)
            return;

        // A reader on Home should learn that the setting is real and their edition ignores it,
        // not that Nostos is broken.
        Assert.Equal("notapplicable.proonly", applicability.ReasonKey);
        Assert.Contains("Home", applicability.Reason);
        Assert.Equal([SystemInfo.Edition], applicability.ReasonArgs);
    }

    [Fact]
    public void The_edition_is_read_from_the_name_Windows_uses_for_itself()
    {
        // EditionID, not ProductName: ProductName still reads "Windows 10 Pro" on a Windows 11
        // machine, which is how a check written against it would decide a Windows 11 Pro box
        // was something it had never heard of.
        Assert.False(string.IsNullOrWhiteSpace(SystemInfo.Edition));
        Assert.Equal(SystemInfo.IsHome(SystemInfo.Edition), SystemInfo.IsHomeEdition);
    }

    [Theory]
    [InlineData("Core", true)]
    [InlineData("CoreN", true)]
    [InlineData("CoreSingleLanguage", true)]
    [InlineData("Professional", false)]
    [InlineData("ProfessionalWorkstation", false)]
    [InlineData("Enterprise", false)]
    [InlineData("Education", false)]
    [InlineData("SomethingMicrosoftShipsNextYear", false)]
    public void Home_is_recognised_by_its_family_and_everything_else_gets_the_benefit_of_the_doubt(
        string edition, bool isHome)
    {
        // The question is asked as "is this Home" rather than "is this Pro or better" on
        // purpose. The edition list is long and grows; an edition this program has never heard
        // of should be offered the tweak, because offering one that turns out to do nothing is
        // a smaller failure than hiding one that would have worked.
        Assert.Equal(isHome, SystemInfo.IsHome(edition));
    }
}
