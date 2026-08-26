using System.Globalization;

namespace Nostos.Cli;

/// <summary>
/// Minimal argument parser.
///
/// Hand-rolled rather than taking System.CommandLine: the CLI is a thin shell over the engine,
/// and keeping the whole product free of NuGet dependencies means a contributor can clone and
/// build offline, and an auditor has less to read.
/// </summary>
public sealed class CommandLine
{
    /// <summary>Flags that never take a value, so a following word stays positional.</summary>
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dry-run", "yes", "all", "no-restore-point", "verbose", "json", "help", "force", "unsafe",
        "service",
    };

    private readonly Dictionary<string, string?> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, string>> _sets = [];

    public List<string> Positional { get; } = [];

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var result = new CommandLine();

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positional.Add(token);
                continue;
            }

            var name = token[2..];
            string? value = null;

            if (name.Contains('=', StringComparison.Ordinal))
            {
                var split = name.Split('=', 2);
                name = split[0];
                value = split[1];
            }
            else if (!BooleanFlags.Contains(name) && i + 1 < args.Count &&
                     !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (string.Equals(name, "set", StringComparison.OrdinalIgnoreCase) && value is not null)
            {
                var pair = value.Split('=', 2);
                result._sets.Add(new KeyValuePair<string, string>(pair[0], pair.Length > 1 ? pair[1] : "true"));
                continue;
            }

            result._flags[name] = value;
        }

        return result;
    }

    public bool Has(string name) => _flags.ContainsKey(name);

    public string? Get(string name) => _flags.GetValueOrDefault(name);

    public int? GetInt(string name)
        => int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public T? GetEnum<T>(string name) where T : struct, Enum
        => Enum.TryParse<T>(Get(name), ignoreCase: true, out var value) ? value : null;

    /// <summary>Values collected from repeated --set key=value arguments.</summary>
    public IReadOnlyDictionary<string, string> Options
    {
        get
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in _sets)
                options[key] = value;
            return options;
        }
    }
}
