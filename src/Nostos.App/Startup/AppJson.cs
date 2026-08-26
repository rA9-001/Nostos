using System.Text.Json.Serialization;

namespace Nostos.App.Startup;

/// <summary>
/// The marker written when the user declines the service, so the app stops asking.
///
/// A record rather than an anonymous object because anonymous types cannot be source-generated,
/// and this file is written on a path that has to work in an ahead-of-time build.
/// </summary>
/// <param name="DeclinedUtc">When the prompt was refused.</param>
/// <param name="Note">Instructions for a user who finds this file and wants to undo it.</param>
public sealed record ServiceDeclineMarker(DateTimeOffset DeclinedUtc, string Note);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ServiceDeclineMarker))]
public sealed partial class AppJsonContext : JsonSerializerContext;
