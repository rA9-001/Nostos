using Nostos.Core.Abstractions;
using Nostos.Tweaks.Native;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Rules for the service optimizer.
///
/// This is the part of the catalog with the most room to do lasting damage, and the damage is
/// the kind nobody traces back: a service turned off in March explains a broken controller in
/// June. So the guarantees are asserted here rather than left to review.
/// </summary>
public sealed class ServiceTweakTests
{
    private static WindowsServiceTweak Build(string serviceName)
        => new(
            id: "services.test",
            serviceName: serviceName,
            title: "Test",
            summary: "Test",
            category: TweakCategories.Unused,
            evidence: Evidence.Plausible);

    [Theory]
    [InlineData("BFE")]          // the firewall, silently
    [InlineData("Audiosrv")]     // all sound
    [InlineData("RpcSs")]        // the machine does not boot
    [InlineData("ProfSvc")]      // nobody can sign in
    [InlineData("bfe")]          // and the check is case-insensitive
    public void A_tweak_cannot_be_declared_against_a_protected_service(string serviceName)
    {
        // Fails while the catalog is being constructed, which means at startup and in every
        // test run -- not at apply time on a user's machine.
        var thrown = Assert.Throws<ArgumentException>(() => Build(serviceName));

        Assert.Contains("protected list", thrown.Message);
    }

    [Theory]
    [InlineData("XboxGipSvc")]
    [InlineData("XblAuthManager")]
    [InlineData("Spooler")]
    [InlineData("bthserv")]
    public void A_service_whose_absence_is_a_preference_is_offered_rather_than_refused(string serviceName)
    {
        // These were on the deny list and are not any more. Turning off Bluetooth or the Xbox
        // stack breaks something, but it breaks something the person doing it knows about and
        // can weigh up -- which is a different thing from breaking sound or boot. Refusing them
        // did not stop anybody; it only pushed them towards sc.exe, where nothing is recorded
        // and nothing can be put back.
        Assert.Null(WindowsServices.ProtectionReason(serviceName));

        var tweak = Build(serviceName);
        Assert.Equal("services.test", tweak.Metadata.Id);
    }

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("Dhcp")]
    public void The_low_level_writer_refuses_protected_services_too(string serviceName)
    {
        // The catalog check above is the early warning. This is the guarantee: even code that
        // never goes through a tweak cannot rewrite one of these.
        var disable = Assert.Throws<InvalidOperationException>(
            () => WindowsServices.SetStartType(serviceName, ServiceStartType.Disabled));
        Assert.Contains("protected list", disable.Message);

        var stop = Assert.Throws<InvalidOperationException>(
            () => WindowsServices.TryStop(serviceName, out _));
        Assert.Contains("protected list", stop.Message);
    }

    [Fact]
    public void Every_protected_entry_explains_what_breaks()
    {
        // The reason string is shown to whoever hit the refusal. "Protected" on its own invites
        // someone to go around it with sc.exe; naming the consequence does not.
        foreach (var (name, reason) in WindowsServices.Protected)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), name);
            Assert.True(reason.Length > 25, $"{name}: reason is too short to be useful");
            Assert.EndsWith(".", reason);
        }
    }

    [Fact]
    public void The_deny_list_still_covers_the_things_that_are_not_a_preference()
    {
        // The list got shorter deliberately. This is the floor it cannot go below: no boot, no
        // sign-in, no network, no sound, no firewall. Nobody weighs those up in advance -- they
        // find out weeks later, with no way to connect it back to a program they once ran.
        string[] floor =
        [
            "RpcSs", "DcomLaunch", "ProfSvc",           // the machine comes back, or does not
            "Dhcp", "Dnscache", "nsi",                  // it has a network, or does not
            "Audiosrv", "AudioEndpointBuilder",         // it has sound, or does not
            "BFE", "mpssvc", "WinDefend",               // it defends itself, or does not
        ];

        foreach (var name in floor)
            Assert.NotNull(WindowsServices.ProtectionReason(name));
    }

    [Theory]
    [InlineData(ServiceStartType.Boot)]
    [InlineData(ServiceStartType.System)]
    public void Driver_start_types_are_not_a_target_this_tool_will_write(ServiceStartType startType)
    {
        // The SCM will happily let you set a boot-start driver to Disabled. The machine then
        // does not come back, and the fix is a recovery console.
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowsServices.SetStartType("Spooler", startType));

        Assert.Contains("drivers", thrown.Message);
    }

    [Fact]
    public void The_default_option_is_manual_and_it_is_the_recommended_one()
    {
        // The whole safety argument for this feature rests on this default. Disabled breaks
        // things in ways that name neither the service nor this tool; Manual does not.
        var choice = Build("Spooler").Metadata.Choices.Single();

        Assert.Equal(WindowsServiceTweak.StartTypeChoice, choice.Id);
        Assert.Equal("manual", choice.DefaultOption);
        Assert.True(choice.Find("manual")!.Recommended);
        Assert.False(choice.Find("disabled")!.Recommended);
    }

    [Fact]
    public void Every_service_tweak_in_the_catalog_defaults_to_manual()
    {
        var services = CatalogFactory.CreateAll().OfType<WindowsServiceTweak>().ToList();

        Assert.NotEmpty(services);
        foreach (var tweak in services)
        {
            var choice = tweak.Metadata.Choices.Single(c => c.Id == WindowsServiceTweak.StartTypeChoice);
            Assert.Equal("manual", choice.DefaultOption);
        }
    }

    [Fact]
    public void Service_tweaks_are_machine_scoped_and_need_elevation()
    {
        foreach (var tweak in CatalogFactory.CreateAll().OfType<WindowsServiceTweak>())
        {
            Assert.Equal(TweakScope.Machine, tweak.Metadata.Scope);
            Assert.True(tweak.Metadata.RequiresElevation, tweak.Metadata.Id);
            Assert.Equal(TweakLifetime.Persistent, tweak.Metadata.Lifetime);
        }
    }

    [Fact]
    public async Task A_service_that_is_not_installed_reads_as_not_applicable()
    {
        // Service names vary by Windows edition and by installed features, so "absent" has to
        // be a normal answer that explains itself rather than an exception.
        var tweak = Build("NoSuchServiceExistsHere");

        var applicability = await tweak.CheckApplicabilityAsync(TweakContext.Default);

        Assert.False(applicability.IsApplicable);
        Assert.Contains("not registered", applicability.Reason);
    }

    [Fact]
    public async Task An_absent_service_reads_as_off_rather_than_throwing()
    {
        var state = await Build("NoSuchServiceExistsHere").ReadAsync(TweakContext.Default);

        Assert.False(state.IsApplied);
        Assert.Contains("not installed", state.Description);
    }

    [Fact]
    public void Querying_an_absent_service_returns_null_rather_than_throwing()
        => Assert.Null(WindowsServices.Query("NoSuchServiceExistsHere"));

    [Fact]
    public void A_real_service_reports_a_start_type_that_is_not_a_driver()
    {
        // Reads the live SCM. Uses the print spooler because it exists on every desktop SKU and
        // this test only reads.
        var info = WindowsServices.Query("Spooler");

        Assert.NotNull(info);
        Assert.Equal("Spooler", info.Name);
        Assert.False(string.IsNullOrWhiteSpace(info.DisplayName));
        Assert.True(
            info.StartType is ServiceStartType.Automatic or ServiceStartType.Manual or ServiceStartType.Disabled,
            $"unexpected start type {info.StartType}");
    }
}
