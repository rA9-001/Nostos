using System.Diagnostics;
using Nostos.Core.Abstractions;
using Nostos.Core.Safety;

namespace Nostos.Win32.Services;

/// <summary>
/// The safety gate used on real machines.
///
/// Before a batch containing anything risky or reboot-requiring, it takes a System Restore
/// point. That is now the only automatic safety net, and it is a deliberately passive one: it
/// puts a recovery option on the machine and then gets out of the way.
///
/// It used to also arm a timer that reverted unconfirmed changes by itself. That is gone. A
/// change this program makes stays made until somebody undoes it, and the recovery paths are
/// the app's revert button, <c>nos revert</c>, and - if the machine will not come back far
/// enough for either - the restore point taken here.
/// </summary>
public sealed class SystemRestoreSafetyGate : ISafetyGate
{
    private readonly ILogSink _log;

    public SystemRestoreSafetyGate(ILogSink? log = null) => _log = log ?? NullLogSink.Instance;

    /// <summary>Set false in tests and on machines where System Protection is off by design.</summary>
    public bool RequireRestorePointForRiskyTweaks { get; init; } = true;

    public async Task<SafetyClearance> BeforeBatchAsync(
        IReadOnlyList<TweakMetadata> batch, CancellationToken ct = default)
    {
        var needsRestorePoint = batch.Any(m =>
            m.Scope == TweakScope.Machine && (m.Risk >= Risk.Risky || m.RequiresReboot));

        if (!needsRestorePoint)
            return SafetyClearance.Allow;

        var description = $"Before {batch.Count} gaming optimizer change(s)";
        var created = await TryCreateRestorePointAsync(description, ct).ConfigureAwait(false);

        if (created)
            return SafetyClearance.Allow;

        if (RequireRestorePointForRiskyTweaks)
        {
            return SafetyClearance.Refuse(
                "could not create a System Restore point, and this batch contains a risky or " +
                "reboot-requiring change. Enable System Protection on the system drive, or re-run " +
                "with --no-restore-point to accept the risk.");
        }

        _log.Warn("proceeding without a restore point because --no-restore-point was given");
        return SafetyClearance.Allow;
    }

    /// <summary>
    /// Creates a restore point via Checkpoint-Computer.
    ///
    /// Shelling out to PowerShell rather than taking a System.Management dependency keeps the
    /// project free of NuGet packages that pull in a large COM surface, and Checkpoint-Computer
    /// already handles the SRRestorePtAPI plumbing correctly.
    /// </summary>
    private async Task<bool> TryCreateRestorePointAsync(string description, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' " +
                "-RestorePointType MODIFY_SETTINGS -ErrorAction Stop");

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                _log.Info($"created System Restore point: {description}");
                return true;
            }

            // Windows rate-limits restore points to one per 24h by default. A recent point is
            // still a usable recovery path, so this is a warning rather than a hard failure.
            _log.Warn($"Checkpoint-Computer failed (exit {process.ExitCode}): {stderr.Trim()}");
            return false;
        }
        catch (Exception e)
        {
            _log.Warn($"could not create a restore point: {e.Message}");
            return false;
        }
    }
}
