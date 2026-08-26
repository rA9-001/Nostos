namespace Nostos.Core;

/// <summary>
/// Well-known locations.
///
/// Machine-scoped state lives under %ProgramData% by default so the SYSTEM service and an
/// unelevated UI read the same journal. Portable installs redirect the whole tree next to the
/// executable with <see cref="UsePortableRoot"/>.
/// </summary>
public static class AppPaths
{
    public const string ProductFolder = "Nostos";

    private static string? _root;

    public static string Root => _root ??= DefaultRoot;

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolder);

    /// <summary>True when state has been redirected out of %ProgramData%.</summary>
    public static bool IsPortable { get; private set; }

    /// <summary>
    /// Redirects all state into <paramref name="root"/>.
    ///
    /// Must be called before anything reads <see cref="Root"/>, which in practice means the
    /// first lines of startup. A portable copy keeps its journal beside the executable, so
    /// moving the folder to another machine carries the record of what was changed with it.
    /// </summary>
    public static void UsePortableRoot(string root)
    {
        if (_root is not null && !string.Equals(_root, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Paths were already resolved; set the portable root earlier.");

        _root = root;
        IsPortable = true;
        EnsureCreated();
    }

    public static string JournalPath => Path.Combine(Root, "journal.jsonl");

    /// <summary>Append-only history of latency measurements. See <c>Benchmark\BenchmarkStore</c>.</summary>
    public static string BenchmarkPath => Path.Combine(Root, "benchmarks.jsonl");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string ProfilesDirectory => Path.Combine(Root, "profiles");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        RemoveLegacyPendingMarker(Root);
    }

    /// <summary>
    /// Deletes the marker left behind by the auto-revert watchdog, which no longer exists.
    ///
    /// A machine that ran an older build still has a "pending.json" in its data folder. Nothing
    /// reads it, but a file with that name sitting next to the journal invites exactly the
    /// wrong conclusion about whether something is still queued to happen, so it goes.
    ///
    /// Takes the root rather than reading <see cref="Root"/> so it can be tested without
    /// pinning the whole process's paths, which <see cref="UsePortableRoot"/> does permanently.
    /// </summary>
    public static void RemoveLegacyPendingMarker(string root)
    {
        try
        {
            File.Delete(Path.Combine(root, "pending.json"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Failing to remove a file nothing reads costs nothing. Throwing here would take
            // down startup over it.
        }
    }
}
