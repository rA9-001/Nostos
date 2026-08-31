using System.Text.Json;
using Nostos.Core.Settings;

namespace Nostos.Core.Tests;

/// <summary>
/// The update cadence, and the on-disk shape of the preferences file.
///
/// Both halves matter for the same reason: this is the file that decides whether a copy of the
/// app ever learns that a fixed version exists. A cadence that silently never comes due is
/// indistinguishable, from the user's side, from an updater that is broken.
/// </summary>
public sealed class SettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);


    [Fact]
    public void A_fresh_install_checks_on_the_first_launch()
    {
        Assert.True(new AppSettings().IsCheckDue(Now));
    }

    [Fact]
    public void Turning_checking_off_stops_it_whatever_the_cadence()
    {
        var settings = new AppSettings { CheckForUpdates = false, Cadence = UpdateCadence.EveryLaunch };

        Assert.False(settings.IsCheckDue(Now));
    }

    [Fact]
    public void Every_launch_means_every_launch()
    {
        var settings = new AppSettings { LastCheckedUtc = Now.AddSeconds(-1) };

        Assert.True(settings.IsCheckDue(Now));
    }

    [Theory]
    [InlineData(UpdateCadence.Daily, 23, false)]
    [InlineData(UpdateCadence.Daily, 25, true)]
    [InlineData(UpdateCadence.Weekly, 24 * 6, false)]
    [InlineData(UpdateCadence.Weekly, 24 * 8, true)]
    public void A_cadence_holds_off_until_its_interval_has_passed(
        UpdateCadence cadence, int hoursAgo, bool due)
    {
        var settings = new AppSettings
        {
            Cadence = cadence,
            LastCheckedUtc = Now.AddHours(-hoursAgo),
        };

        Assert.Equal(due, settings.IsCheckDue(Now));
    }

    [Fact]
    public void A_check_stamped_in_the_future_does_not_disable_checking()
    {
        // Restoring a VM snapshot, fixing a timezone, or a dead CMOS battery all put a stamp
        // ahead of the clock. Waiting for real time to catch up would turn a wrong clock into
        // an app that never checks again -- silently, and for as long as the skew lasts.
        var settings = new AppSettings
        {
            Cadence = UpdateCadence.Weekly,
            LastCheckedUtc = Now.AddYears(3),
        };

        Assert.True(settings.IsCheckDue(Now));
    }

    [Fact]
    public void The_file_stores_the_cadence_by_name()
    {
        // Not by number. Somebody opening this file to find out why their copy stopped asking
        // about updates should be able to read the answer, and reordering the enum must not
        // silently reinterpret a file written by an older build.
        var json = JsonSerializer.Serialize(
            new AppSettings { Cadence = UpdateCadence.Weekly },
            SettingsJsonContext.Default.AppSettings);

        Assert.Contains("Weekly", json);
    }

    [Fact]
    public void A_settings_file_from_an_older_build_keeps_the_defaults_for_what_it_lacks()
    {
        // The trap documented in CoreJson: a property missing from the JSON must come back as
        // its declared default, not as false or null.
        var settings = JsonSerializer.Deserialize(
            """{"lastCheckedUtc":"2026-08-26T12:00:00+00:00"}""",
            SettingsJsonContext.Default.AppSettings);

        Assert.NotNull(settings);
        Assert.True(settings.UpdateChecksEnabled);
        Assert.Equal(UpdateCadence.EveryLaunch, settings.Cadence);
    }

    [Fact]
    public void Settings_survive_a_round_trip()
    {
        var original = new AppSettings
        {
            CheckForUpdates = false,
            Cadence = UpdateCadence.Daily,
            Language = Nostos.Core.Localization.Language.German,
            LastCheckedUtc = Now,
        };

        var back = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, SettingsJsonContext.Default.AppSettings),
            SettingsJsonContext.Default.AppSettings);

        Assert.Equal(original, back);
    }
}
