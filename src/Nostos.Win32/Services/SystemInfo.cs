using System.Security.Principal;
using Nostos.Core.Abstractions;
using Nostos.Win32.Interop;

namespace Nostos.Win32.Services;

/// <summary>Smart App Control state, which decides whether unsigned builds can run at all.</summary>
public enum SmartAppControlState
{
    Off = 0,
    Enforced = 1,
    Evaluation = 2,
    Unknown = -1,
}

public sealed class WindowsPrivilegeCheck : IPrivilegeCheck
{
    public static readonly WindowsPrivilegeCheck Instance = new();

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

public static class SystemInfo
{
    public static Version OsVersion => Environment.OSVersion.Version;

    public static int Build => OsVersion.Build;

    public static bool IsWindows11 => Build >= 22000;

    /// <summary>Update Build Revision, the number after the build, e.g. the 2100 in 22631.2100.</summary>
    public static int UpdateBuildRevision => RegistryAccess.ReadDword(
        new RegistryValueRef("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR")) ?? 0;

    /// <summary>
    /// The edition as Windows names it internally: "Core" on Home, "Professional",
    /// "Enterprise", "Education". Not the marketing name -- ProductName still reads
    /// "Windows 10 Pro" on a Windows 11 machine, which is why nothing here uses it.
    /// </summary>
    public static string Edition
    {
        get
        {
            var (value, _) = RegistryAccess.Read(
                new RegistryValueRef("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID"));
            return value?.ToString() ?? "";
        }
    }

    /// <summary>
    /// True on Home, which ignores the Windows Update for Business policies outright.
    ///
    /// Deliberately asked as "is this Home" rather than "is this Pro or better": the edition
    /// list is long and Microsoft keeps adding to it, and an edition this program has never
    /// heard of should get the tweak offered rather than hidden. Offering one that turns out
    /// to do nothing is a smaller failure than hiding one that would have worked.
    /// </summary>
    public static bool IsHomeEdition => IsHome(Edition);

    /// <summary>The rule behind <see cref="IsHomeEdition"/>, separated so it can be tested.</summary>
    public static bool IsHome(string editionId)
        => editionId.StartsWith("Core", StringComparison.OrdinalIgnoreCase);

    public static string DisplayVersion
    {
        get
        {
            var (value, _) = RegistryAccess.Read(
                new RegistryValueRef("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion"));
            return value?.ToString() ?? "unknown";
        }
    }

    /// <summary>
    /// True when this machine has a battery. Several power tweaks are a straight regression on
    /// laptops, so the catalog checks this rather than assuming a desktop.
    /// </summary>
    public static bool HasBattery
    {
        get
        {
            if (!NativeMethods.GetSystemPowerStatus(out var status))
                return false;
            const byte noSystemBattery = 128;
            return (status.BatteryFlag & noSystemBattery) == 0;
        }
    }

    public static bool IsOnBatteryPower
    {
        get
        {
            if (!NativeMethods.GetSystemPowerStatus(out var status))
                return false;
            const byte offline = 0;
            return status.AcLineStatus == offline;
        }
    }

    /// <summary>
    /// Smart App Control blocks unsigned executables outright on machines where it is enforced.
    /// Detecting it lets the app explain itself instead of just failing to start.
    /// </summary>
    public static SmartAppControlState SmartAppControl
    {
        get
        {
            var value = RegistryAccess.ReadDword(new RegistryValueRef(
                "HKLM", @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState"));
            return value switch
            {
                0 => SmartAppControlState.Off,
                1 => SmartAppControlState.Enforced,
                2 => SmartAppControlState.Evaluation,
                _ => SmartAppControlState.Unknown,
            };
        }
    }

    /// <summary>Current timer resolution in milliseconds, as the kernel reports it.</summary>
    public static double CurrentTimerResolutionMs
    {
        get
        {
            NativeMethods.NtQueryTimerResolution(out _, out _, out var current);
            return current / 10_000.0;
        }
    }
}
