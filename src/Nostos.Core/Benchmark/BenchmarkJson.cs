using System.Text.Json.Serialization;

namespace Nostos.Core.Benchmark;

/// <summary>
/// Serializer for the benchmark history.
///
/// Its own context rather than a member of <c>CoreJson</c>, for the same reason the journal and
/// the profiles have their own: this is a distinct file format with its own compatibility
/// promise. It is append-only and must stay compact -- a run carries a couple of hundred
/// samples, and indenting them would multiply the file size for nothing, because nobody reads
/// this file by hand.
///
/// Note the trap documented in <c>CoreJson</c> applies here too: <see cref="BenchmarkRun"/> has
/// `required` members, so its property initializers never run during deserialization. The
/// null-coalescing init accessors on <c>Label</c> and <c>AppliedTweaks</c> are what actually
/// keeps them non-null on the way back in.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(BenchmarkRun))]
public sealed partial class BenchmarkJson : JsonSerializerContext;
