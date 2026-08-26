using Nostos.Core.Abstractions;

namespace Nostos.Cli;

public sealed class ConsoleLog : ILogSink
{
    private readonly bool _verbose;

    public ConsoleLog(bool verbose) => _verbose = verbose;

    public void Log(LogLevel level, string message, Exception? error = null)
    {
        if (level == LogLevel.Debug && !_verbose)
            return;

        var (prefix, colour) = level switch
        {
            LogLevel.Debug => ("debug", Ansi.Dim),
            LogLevel.Info => (" info", Ansi.Reset),
            LogLevel.Warn => (" warn", Ansi.Yellow),
            _ => ("error", Ansi.Red),
        };

        var writer = level == LogLevel.Error ? Console.Error : Console.Out;
        writer.WriteLine($"{colour}{prefix}{Ansi.Reset}  {message}");

        if (error is not null && _verbose)
            writer.WriteLine(error);
    }
}

/// <summary>
/// ANSI colour codes, suppressed when output is redirected so piping to a file or a CI log
/// produces clean text.
/// </summary>
public static class Ansi
{
    private static readonly bool Enabled = !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    private const string Esc = "\x1b";

    private static string Code(string code) => Enabled ? Esc + code : "";

    public static string Reset => Code("[0m");
    public static string Dim => Code("[90m");
    public static string Red => Code("[31m");
    public static string Green => Code("[32m");
    public static string Yellow => Code("[33m");
    public static string Cyan => Code("[36m");
    public static string Bold => Code("[1m");
}
