namespace Nostos.Core.Abstractions;

/// <summary>Everything a tweak needs from the outside world for one operation.</summary>
public sealed class TweakContext
{
    public static TweakContext Default { get; } = new();

    /// <summary>Tweak-specific parameters, e.g. {"value": "10"}. Comes from the profile or the CLI.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Target for <see cref="TweakScope.Process"/> tweaks. Ignored otherwise.</summary>
    public int? TargetProcessId { get; init; }

    /// <summary>Friendly name of the target process, for logging.</summary>
    public string? TargetProcessName { get; init; }

    /// <summary>When true, Apply and Revert must log what they would do and change nothing.</summary>
    public bool DryRun { get; init; }

    public ILogSink Log { get; init; } = NullLogSink.Instance;

    public TweakContext With(IReadOnlyDictionary<string, string> options) => new()
    {
        Options = options,
        TargetProcessId = TargetProcessId,
        TargetProcessName = TargetProcessName,
        DryRun = DryRun,
        Log = Log,
    };

    public TweakContext ForProcess(int pid, string? name) => new()
    {
        Options = Options,
        TargetProcessId = pid,
        TargetProcessName = name,
        DryRun = DryRun,
        Log = Log,
    };

    public string? GetString(string key)
        => Options.TryGetValue(key, out var v) ? v : null;

    public int GetInt(string key, int fallback)
        => Options.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    public bool GetBool(string key, bool fallback)
        => Options.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
}
