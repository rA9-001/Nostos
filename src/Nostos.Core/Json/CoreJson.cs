using System.Text.Json.Serialization;
using Nostos.Core.Journal;
using Nostos.Core.Profiles;

namespace Nostos.Core.Json;

// Source-generated serializers, one context per on-disk format.
//
// These exist so the app can be compiled ahead of time: reflection-based System.Text.Json needs
// to emit code at runtime, which NativeAOT has no way to do. The generator writes the
// marshalling code at build time instead.
//
// The formats are kept in separate contexts rather than merged into one, because each is a
// distinct file format with its own compatibility promise. The journal must stay compact and
// append-only; the profiles are hand-edited and must tolerate comments and trailing commas.
// Merging them would force one set of options onto both.
//
// Adding a type to a journal or profile record means adding nothing here -- the generator walks
// the graph -- but a NEW top-level type does need its own [JsonSerializable] entry, or it will
// throw at the first call rather than falling back to reflection.
//
// One trap worth knowing about, because it is silent. When a record has any `required` member,
// the generator builds the whole object from a single argument array rather than constructing
// it and then assigning properties. Every property absent from the JSON is passed as `default`,
// so a C# property initializer on that record NEVER RUNS during deserialization: `= []` comes
// back null, `= "manual"` comes back null. Nothing warns you; the first `foreach` over it
// throws, possibly weeks later on someone else's machine.
//
// The fix used throughout these records is a null-coalescing init accessor,
//
//     public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];
//
// which holds no matter how the object was constructed, and additionally survives an explicit
// `"tags": null` on the wire -- something the reflection-based serializer did not handle
// either. Keep the trailing initializer: it is what covers objects built in C# rather than
// parsed. See CatalogParsingTests for the regression tests.

/// <summary>
/// The journal line format: compact, enums as names.
///
/// Enums are written as strings on purpose. The journal is the file someone reads with `type`
/// on a machine that will not boot, and "ApplyIntent" tells them something that "0" does not.
/// It also means reordering the enum cannot silently reinterpret old entries.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(JournalEntry))]
public sealed partial class JournalJsonContext : JsonSerializerContext;

/// <summary>
/// The profile format: hand-editable, so forgiving on input and readable on output.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(TweakProfile))]
public sealed partial class ProfileJsonContext : JsonSerializerContext;
