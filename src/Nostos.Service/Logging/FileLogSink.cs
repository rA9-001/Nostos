using System.Text;
using Nostos.Core;
using Nostos.Core.Abstractions;

namespace Nostos.Service.Logging;

/// <summary>
/// Day-rotated text log under %ProgramData%.
///
/// A service that changes machine state has to leave a readable trail somewhere a user can
/// find without tooling. The journal records *what changed*; this records *what the service
/// was doing*, including the decisions that resulted in no change at all.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly Lock _gate = new();
    private readonly bool _echoToConsole;
    private readonly LogLevel _minimum;
    private readonly int _retentionDays;

    private StreamWriter? _writer;
    private DateOnly _openedFor;

    public FileLogSink(bool echoToConsole = false, LogLevel minimum = LogLevel.Info, int retentionDays = 14)
    {
        _echoToConsole = echoToConsole;
        _minimum = minimum;
        _retentionDays = retentionDays;
        AppPaths.EnsureCreated();
        PruneOldLogs();
    }

    public void Log(LogLevel level, string message, Exception? error = null)
    {
        if (level < _minimum)
            return;

        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z  {level.ToString().ToUpperInvariant(),-5}  {message}";

        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer?.WriteLine(line);
                if (error is not null)
                    _writer?.WriteLine(error);
                _writer?.Flush();
            }
            catch (IOException)
            {
                // Losing a log line must never take down the service.
            }
        }

        if (_echoToConsole)
        {
            var writer = level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(line);
            if (error is not null)
                writer.WriteLine(error);
        }
    }

    private void EnsureWriter()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_writer is not null && _openedFor == today)
            return;

        _writer?.Dispose();
        var path = Path.Combine(AppPaths.LogsDirectory, $"service-{today:yyyyMMdd}.log");
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false));
        _openedFor = today;
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDirectory, "service-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
