using Microsoft.Win32;
using Nostos.Core.Abstractions;
using Nostos.Tweaks.Declarative;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Parsing rules for the contributor-facing catalog format.
///
/// These are about what happens when a field is <em>missing</em>, which is the normal case:
/// most tweaks declare no tags and no conflicts. A property that comes back null instead of
/// empty turns every loop over it into a NullReferenceException, and one that comes back as
/// default instead of the documented default silently changes what the tweak does.
/// </summary>
public sealed class CatalogParsingTests
{
    private const string Minimal = """
        [
          {
            "id": "test.minimal",
            "title": "Minimal",
            "summary": "Only the required fields.",
            "category": "performance",
            "risk": "Safe",
            "evidence": "Plausible",
            "values": [
              { "hive": "HKLM", "key": "SOFTWARE\\Test", "name": "V", "kind": "DWord", "value": "1" }
            ]
          }
        ]
        """;

    [Fact]
    public void Optional_collections_default_to_empty_when_absent()
    {
        var definition = RegistryTweakCatalog.Parse(Minimal).Single();

        Assert.Empty(definition.Tags);
        Assert.Empty(definition.ConflictsWith);
    }

    [Fact]
    public void Optional_collections_survive_an_explicit_null()
    {
        var withNulls = Minimal.Replace(
            "\"category\": \"performance\",",
            "\"category\": \"performance\", \"tags\": null, \"conflictsWith\": null,",
            StringComparison.Ordinal);

        var definition = RegistryTweakCatalog.Parse(withNulls).Single();

        Assert.Empty(definition.Tags);
        Assert.Empty(definition.ConflictsWith);
    }

    [Fact]
    public void Metadata_carries_the_empty_collections_through()
    {
        // The engine reads Metadata, not the definition, so the guarantee has to survive the copy.
        var metadata = RegistryTweakCatalog.Parse(Minimal).Single().ToMetadata();

        Assert.Empty(metadata.Tags);
        Assert.Empty(metadata.ConflictsWith);
    }

    [Fact]
    public void Documented_defaults_apply_to_absent_scalar_fields()
    {
        var definition = RegistryTweakCatalog.Parse(Minimal).Single();

        Assert.Equal(Core.Abstractions.TweakScope.Machine, definition.Scope);
        Assert.Equal(Core.Abstractions.TweakLifetime.Persistent, definition.Lifetime);
        Assert.Equal(Microsoft.Win32.RegistryValueKind.DWord, definition.Values[0].Kind);
        Assert.True(definition.EffectiveRequiresElevation);
    }

    [Fact]
    public void An_absent_optional_string_keeps_its_default_rather_than_going_null()
    {
        // RegistryValueSpec.Name defaults to "" and means "the key's default value". A null
        // here would reach the registry API as a different request entirely.
        var noName = Minimal.Replace("\"name\": \"V\", ", "", StringComparison.Ordinal);

        var definition = RegistryTweakCatalog.Parse(noName).Single();

        Assert.Equal("", definition.Values[0].Name);
    }

    /// <summary>
    /// The encoded form is the contract with the snapshot file, so it has to stay decimal and
    /// exactly round-trippable even when that reads badly.
    /// </summary>
    [Theory]
    [InlineData("0xFFFFFFFF", "-1")]
    [InlineData("0x80000001", "-2147483647")]
    [InlineData("10", "10")]
    public void A_dword_is_encoded_as_signed_decimal_whatever_the_catalog_wrote(string written, string encoded)
    {
        var value = RegistryAccess.Decode(written, RegistryValueKind.DWord);

        Assert.Equal(encoded, RegistryAccess.Encode(value, RegistryValueKind.DWord));
    }

    /// <summary>
    /// ...and the displayed form is allowed to differ, because "-2147483647" beside a tweak
    /// called "timestamps off" reads as a bug rather than as 0x80000001.
    /// </summary>
    [Theory]
    [InlineData("0xFFFFFFFF", "0xFFFFFFFF")]
    [InlineData("0x80000001", "0x80000001")]
    [InlineData("0x20", "32")]
    [InlineData("10", "10")]
    [InlineData("0", "0")]
    public void A_dword_with_the_top_bit_set_is_shown_as_hex_and_the_rest_as_decimal(string written, string shown)
    {
        var value = RegistryAccess.Decode(written, RegistryValueKind.DWord);

        Assert.Equal(shown, RegistryAccess.Describe(value, RegistryValueKind.DWord));
    }

    [Fact]
    public void Describing_a_non_dword_is_the_same_as_encoding_it()
    {
        // Only DWORDs have the signed/unsigned problem. Strings must not be reformatted, or a
        // state line stops matching the value a user would see in regedit.
        Assert.Equal("506", RegistryAccess.Describe("506", RegistryValueKind.String));
        Assert.Equal("a\nb", RegistryAccess.Describe(new[] { "a", "b" }, RegistryValueKind.MultiString));
    }

    // --------------------------------------------------------- services.json

    [Fact]
    public void A_service_entry_needs_only_six_fields()
    {
        var definitions = ServiceTweakCatalog.Parse("""
            [
              {
                "id": "services.test",
                "service": "TestSvc",
                "title": "Stop the test service starting at boot",
                "summary": "Exists only in this test.",
                "category": "unused",
                "evidence": "Plausible"
              }
            ]
            """);

        var definition = Assert.Single(definitions);
        Assert.Equal("TestSvc", definition.Service);

        // The two defaults that are not written down in the file. Both are the cautious answer:
        // a service tweak nobody has rated is Moderate, and it is tagged as a service.
        Assert.Null(definition.Risk);
        Assert.Equal(["service"], definition.Tags);
        Assert.Equal(Risk.Moderate, definition.ToTweak().Metadata.Risk);
    }

    [Fact]
    public void A_service_entry_that_does_not_say_what_its_evidence_is_refuses_to_load()
    {
        // This is the trap that made AppSettings.CheckForUpdates nullable, in the other
        // direction. The source generator passes `default` for a missing property, and
        // `default(Evidence)` is Measured -- the strongest claim in the vocabulary, handed out
        // for free to an entry whose author never made it. Failing to load is the only safe
        // reading of silence.
        Assert.ThrowsAny<Exception>(() => ServiceTweakCatalog.Parse("""
            [
              {
                "id": "services.test",
                "service": "TestSvc",
                "title": "Stop the test service starting at boot",
                "summary": "Says nothing about evidence.",
                "category": "unused"
              }
            ]
            """));
    }

    [Fact]
    public void Every_service_tweak_that_ships_comes_from_the_data_file()
    {
        // Cheap, but it is the test that fails loudly if services.json stops being embedded --
        // which would otherwise show up as a catalog that is quietly 35 tweaks shorter.
        var definitions = ServiceTweakCatalog.LoadEmbedded();

        Assert.NotEmpty(definitions);
        Assert.All(definitions, d => Assert.StartsWith("services.", d.Id, StringComparison.Ordinal));
        Assert.Equal(
            definitions.Count,
            CatalogFactory.CreateAll().OfType<Nostos.Tweaks.Native.WindowsServiceTweak>().Count());
    }

    // --------------------------------------------------------- adapters.json

    [Fact]
    public void An_adapter_entry_parses_its_keyword_list()
    {
        var definitions = AdapterTweakCatalog.Parse("""
            [
              {
                "id": "network.test",
                "title": "Stop the test thing",
                "summary": "Exists only in this test.",
                "keywords": ["*One", "*Two"],
                "target": "0",
                "absentReason": "no adapter here has it"
              }
            ]
            """);

        var definition = Assert.Single(definitions);
        Assert.Equal(["*One", "*Two"], definition.Keywords);
        Assert.Empty(definition.Tags);
    }

    [Fact]
    public void Every_adapter_tweak_that_ships_comes_from_the_data_file()
    {
        var definitions = AdapterTweakCatalog.LoadEmbedded();

        Assert.NotEmpty(definitions);
        Assert.All(definitions, d => Assert.NotEmpty(d.Keywords));
        Assert.Equal(
            definitions.Count,
            CatalogFactory.CreateAll().OfType<Nostos.Tweaks.Native.NetworkAdapterTweak>().Count());
    }
}
