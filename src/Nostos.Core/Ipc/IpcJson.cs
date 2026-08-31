using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nostos.Core.Abstractions;
using System.Text.Json.Serialization.Metadata;

namespace Nostos.Ipc;

/// <summary>
/// Source-generated serializers for the wire contract.
///
/// Every type that can cross the pipe is listed here. That is a feature rather than a chore:
/// the pipe is a privilege boundary into a process that can rewrite HKLM, and an explicit list
/// of what may cross it is exactly the property you want. A type that is not declared fails
/// loudly at the boundary instead of being quietly serialized by reflection.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(ChangeRequest))]
[JsonSerializable(typeof(ChangeResult))]
[JsonSerializable(typeof(TweakSummary))]
[JsonSerializable(typeof(TweakChoice))]
[JsonSerializable(typeof(TweakChoiceOption))]
[JsonSerializable(typeof(TweakStatusSummary))]
[JsonSerializable(typeof(JournalRequest))]
[JsonSerializable(typeof(JournalLine))]
[JsonSerializable(typeof(ProfileSummary))]
[JsonSerializable(typeof(ApplyProfileRequest))]
[JsonSerializable(typeof(List<ChangeResult>))]
[JsonSerializable(typeof(List<TweakSummary>))]
[JsonSerializable(typeof(List<TweakStatusSummary>))]
[JsonSerializable(typeof(List<JournalLine>))]
[JsonSerializable(typeof(List<ProfileSummary>))]
[JsonSerializable(typeof(StartupEntry))]
[JsonSerializable(typeof(StartupSetRequest))]
[JsonSerializable(typeof(StartupSetResult))]
[JsonSerializable(typeof(List<StartupEntry>))]
[JsonSerializable(typeof(string))]
public sealed partial class IpcJsonContext : JsonSerializerContext;

public static class IpcJson
{
    /// <summary>
    /// Metadata for a type on the wire, or a clear failure.
    ///
    /// The throw is deliberate. The reflection-based fallback it replaces would have worked at
    /// runtime and then failed only in an ahead-of-time build, which is the worst possible place
    /// to find out. This way an undeclared payload type breaks the first test that sends one.
    /// </summary>
    /// <summary>
    /// Converts a payload to the JSON it travels as.
    ///
    /// A value that is already a <see cref="JsonNode"/> passes straight through. Without that,
    /// a caller that had serialized its own result would have it serialized a second time, and
    /// the type lookup would fail on an internal node type it has never heard of -- which is a
    /// confusing way to find out you double-encoded something.
    /// </summary>
    public static JsonNode? ToNode(object? payload) => payload switch
    {
        null => null,
        JsonNode node => node,
        _ => JsonSerializer.SerializeToNode(payload, TypeInfo(payload.GetType())),
    };

    public static JsonTypeInfo TypeInfo(Type type)
        => IpcJsonContext.Default.GetTypeInfo(type)
           ?? throw new InvalidOperationException(
               $"'{type}' is not declared in {nameof(IpcJsonContext)}, so it cannot cross the " +
               "control pipe. Add a [JsonSerializable] entry for it.");
}
