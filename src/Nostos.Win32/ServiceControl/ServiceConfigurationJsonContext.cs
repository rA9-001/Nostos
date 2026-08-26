using System.Text.Json.Serialization;

namespace Nostos.Win32.ServiceControl;

/// <summary>Source-generated reader for service.json, so the app can be AOT-compiled.</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(ServiceConfiguration))]
public sealed partial class ServiceConfigurationJsonContext : JsonSerializerContext;
