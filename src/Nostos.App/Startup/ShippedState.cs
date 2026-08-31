using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core;

namespace Nostos.App.Startup;

/// <summary>
/// What this program last wrote into the profiles folder, so it can tell its own files from the
/// user's.
///
/// The folder holds two kinds of file that look identical: the profiles we ship, and profiles
/// the user wrote or edited. We are entitled to replace the first kind when a release improves
/// one, and not entitled to touch the second. Nothing on disk distinguishes them, so this
/// records the hash of every file we write; a file whose hash still matches is one nobody has
/// touched since, and a file whose hash does not is theirs.
///
/// Deliberately not a list of "our" names. The user is invited to edit these files, and editing
/// <c>basic.json</c> must not mean the next release silently reverts it — which is exactly what
/// deciding by name would do.
///
/// Losing this file is safe in the direction that matters: every profile then looks edited, so
/// nothing is overwritten. The cost is that an improved profile stops arriving, which is the
/// behaviour this whole mechanism replaced.
/// </summary>
internal sealed class ShippedState
{
    private static string Path => System.IO.Path.Combine(AppPaths.Root, "shipped-profiles.json");

    private readonly Dictionary<string, string> _hashes;

    private ShippedState(Dictionary<string, string> hashes) => _hashes = hashes;

    public static string HashOf(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static ShippedState Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new ShippedState(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var stored = JsonSerializer.Deserialize(
                File.ReadAllText(Path), ShippedStateJsonContext.Default.DictionaryStringString);

            return new ShippedState(stored is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable is the same as absent, and absent means "assume everything is theirs".
            return new ShippedState(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>True when the file on disk is byte for byte what we last wrote there.</summary>
    public bool WasWrittenByUs(string fileName, string currentHash)
        => _hashes.TryGetValue(fileName, out var written)
           && string.Equals(written, currentHash, StringComparison.OrdinalIgnoreCase);

    public void Record(string fileName, string hash) => _hashes[fileName] = hash;

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();

            // Atomic, for the same reason the profiles themselves are: an interrupted write
            // here leaves a file that parses as nothing, and then every shipped profile looks
            // edited forever.
            var temporary = Path + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(_hashes, ShippedStateJsonContext.Default.DictionaryStringString));

            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a launch over. The next run re-reads the old record, or none.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class ShippedStateJsonContext : JsonSerializerContext;
