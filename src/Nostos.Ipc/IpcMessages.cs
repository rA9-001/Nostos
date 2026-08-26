using System.Text.Json;
using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;

namespace Nostos.Ipc;

public sealed record IpcRequest
{
    public required string Id { get; init; }
    public required string Command { get; init; }
    public JsonNode? Payload { get; init; }

    public T? PayloadAs<T>() where T : class => Payload is null
        ? null
        : (T?)JsonSerializer.Deserialize(Payload, IpcJson.TypeInfo(typeof(T)));

    public static IpcRequest Create(string command, object? payload = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Command = command,
        Payload = IpcJson.ToNode(payload),
    };
}

public sealed record IpcResponse
{
    public required string Id { get; init; }
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public JsonNode? Result { get; init; }

    public T? ResultAs<T>() where T : class => Result is null
        ? null
        : (T?)JsonSerializer.Deserialize(Result, IpcJson.TypeInfo(typeof(T)));

    public static IpcResponse Success(string id, object? result = null) => new()
    {
        Id = id,
        Ok = true,
        Result = IpcJson.ToNode(result),
    };

    public static IpcResponse Failure(string id, string error) => new()
    {
        Id = id,
        Ok = false,
        Error = error,
    };
}

// ------------------------------------------------------------------- payloads

public sealed record PingResult(
    int ProtocolVersion,
    string ServiceVersion,
    int ProcessId,
    int CatalogSize,
    int OutstandingChanges);

/// <param name="TweakIds">Tweaks to act on. Ignored when <paramref name="All"/> is set on a revert.</param>
/// <param name="Options">Tweak parameters, e.g. {"priority": "High"}.</param>
/// <param name="TargetProcessId">Required for process-scoped tweaks.</param>
/// <param name="Origin">Free-form provenance tag recorded in the journal.</param>
public sealed record ChangeRequest
{
    public IReadOnlyList<string> TweakIds { get; init => field = value ?? []; } = [];

    public IReadOnlyDictionary<string, string> Options
    {
        get;
        init => field = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int? TargetProcessId { get; init; }
    public string? TargetProcessName { get; init; }
    public bool DryRun { get; init; }
    public bool All { get; init; }
    public string Origin { get; init => field = value ?? "ipc"; } = "ipc";
}

/// <summary>Flattened <see cref="TweakOperationResult"/>; the exception never crosses the pipe.</summary>
public sealed record ChangeResult(
    string TweakId,
    Outcome Outcome,
    string Message,
    bool RequiresReboot);

public sealed record TweakSummary(
    string Id,
    string Title,
    string Summary,
    string Category,
    TweakScope Scope,
    TweakLifetime Lifetime,
    Risk Risk,
    Evidence Evidence,
    bool RequiresReboot,
    bool RequiresElevation,
    // Core's own type crosses the wire unchanged. A parallel DTO would be a second place to
    // forget to add an option's description, which is the one field that must not go missing.
    IReadOnlyList<TweakChoice> Choices);

public sealed record TweakStatusSummary(
    TweakSummary Tweak,
    bool IsApplied,
    bool IsManagedByUs,
    string StateDescription,
    bool IsApplicable,
    string? NotApplicableReason);

public sealed record JournalRequest(int Tail = 30);

public sealed record JournalLine(
    DateTimeOffset TimestampUtc,
    string TweakId,
    string Action,
    string Origin,
    string? Detail,
    string? Error);

public sealed record ProfileSummary(
    string Name,
    string Description,
    int TweakCount);

public sealed record ApplyProfileRequest(string Name, bool DryRun = false);
