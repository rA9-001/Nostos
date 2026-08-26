using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core;

namespace Nostos.Win32.ServiceControl;

/// <summary>
/// Service settings, written at install time and read by the daemon.
///
/// <see cref="AllowedSids"/> is the security-critical field: it is the list of accounts allowed
/// to drive a LocalSystem process that can rewrite HKLM. It is populated with the installing
/// user, never with a broad group like Users or Everyone.
/// </summary>
public sealed record ServiceConfiguration
{
    public static string Path => System.IO.Path.Combine(AppPaths.Root, "service.json");

    /// <summary>SIDs permitted to connect to the control pipe, besides SYSTEM and Administrators.</summary>
    public IReadOnlyList<string> AllowedSids { get; init => field = value ?? []; } = [];

    /// <summary>How often to re-apply tweaks that Windows has reset behind our back.</summary>
    public int ReconcileMinutes { get; init; } = 30;

    public static ServiceConfiguration Load()
    {
        if (!File.Exists(Path))
            return new ServiceConfiguration();

        try
        {
            return JsonSerializer.Deserialize(
                       File.ReadAllText(Path), ServiceConfigurationJsonContext.Default.ServiceConfiguration)
                   ?? new ServiceConfiguration();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Falling back to defaults would silently open the pipe to nobody, which is the
            // safe direction: the service still runs, but only SYSTEM and admins can drive it.
            return new ServiceConfiguration();
        }
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            this, ServiceConfigurationJsonContext.Default.ServiceConfiguration));
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>SID of the account running the installer, so the UI can reach the service later.</summary>
    public static string CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
               ?? throw new InvalidOperationException("Could not determine the current user's SID.");
    }
}
