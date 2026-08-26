namespace Nostos.Core.Abstractions;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Minimal logging seam. Deliberately hand-rolled rather than taking a dependency on
/// Microsoft.Extensions.Logging.Abstractions: the tweak catalog must stay dependency-free
/// so that a contributor adding a tweak never has to think about the DI container.
/// </summary>
public interface ILogSink
{
    void Log(LogLevel level, string message, Exception? error = null);
}

public static class LogSinkExtensions
{
    public static void Debug(this ILogSink log, string message) => log.Log(LogLevel.Debug, message);
    public static void Info(this ILogSink log, string message) => log.Log(LogLevel.Info, message);
    public static void Warn(this ILogSink log, string message) => log.Log(LogLevel.Warn, message);
    public static void Error(this ILogSink log, string message, Exception? e = null) => log.Log(LogLevel.Error, message, e);
}

public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();
    public void Log(LogLevel level, string message, Exception? error = null) { }
}
