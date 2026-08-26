using System.Text.Json.Serialization;

namespace Nostos.Tweaks.Declarative;

/// <summary>
/// Source-generated reader for the declarative catalog, so the app can be AOT-compiled.
///
/// Forgiving on input by design: the catalog is a file contributors hand-edit, and rejecting a
/// pull request over a trailing comma helps nobody. Enum names rather than numbers for the same
/// reason -- a diff that reads "Risk": "Moderate" can be reviewed; one that reads "Risk": 2
/// cannot.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<RegistryTweakDefinition>))]
public sealed partial class CatalogJsonContext : JsonSerializerContext;
