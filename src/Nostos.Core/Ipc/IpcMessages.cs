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
    IReadOnlyList<TweakChoice> Choices,
    // Whether the window should offer a process picker. Optional on the wire, and the default
    // is what the window used to work out for itself, so a service built before this field
    // existed still gets a picker on every process-scoped tweak -- just not on the one
    // machine-scoped tweak that wants one.
    bool? TakesTargetProcess = null,
    // Carried so the window can group tweaks by something other than category. The Windows
    // Update tab is drawn from the "windows-update" tag, and the tweaks that carry it sit in
    // three different categories on purpose: a category is a claim about what a tweak does for
    // the player, and "which part of Windows it writes to" is not one of those.
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>True when this tweak has to be told which program it is about.</summary>
    public bool NeedsTarget => TakesTargetProcess ?? Scope == TweakScope.Process;

    /// <summary>Never null, so a caller can filter without a null check first.</summary>
    public IReadOnlyList<string> TagList => Tags ?? [];

    public bool HasTag(string tag) => TagList.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

public sealed record TweakStatusSummary(
    TweakSummary Tweak,
    bool IsApplied,
    bool IsManagedByUs,
    string StateDescription,
    bool IsApplicable,
    string? NotApplicableReason,
    // The key that translates NotApplicableReason, and what to put in it. Optional on the wire
    // in both directions: a service built before these existed sends neither and the window
    // shows the English, which is exactly the behaviour that field had on its own.
    string? NotApplicableReasonKey = null,
    IReadOnlyList<string>? NotApplicableReasonArgs = null);

/// <summary>
/// One thing that runs when you sign in.
///
/// The wire shape mirrors <c>Nostos.Win32.Services.StartupItem</c> rather than reusing it,
/// because Core has no Windows dependency and the protocol has to stay readable without one.
/// <paramref name="Source"/> travels as its string name for the same reason.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>user-run:Steam</c>. What StartupSet names.</param>
/// <param name="IsMachineWide">True when switching it affects every account, and so needs the service.</param>
public sealed record StartupEntry(
    string Id,
    string Name,
    string Source,
    string Command,
    string? ExecutablePath,
    bool IsEnabled,
    string Location,
    bool IsMachineWide);

/// <param name="Id">The entry to switch, as it came back from StartupList.</param>
/// <param name="Enabled">What it should be afterwards.</param>
public sealed record StartupSetRequest(string Id, bool Enabled);

/// <param name="Ok">False when the entry was not found or the write was refused.</param>
public sealed record StartupSetResult(string Id, bool Ok, string Message);

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
    int TweakCount,
    // What it would apply, in the order the profile lists it. Optional on the wire so a service
    // built before this sends nothing and the window shows the count on its own, which is what
    // it showed before there was a list to open.
    IReadOnlyList<string>? TweakIds = null);

public sealed record ApplyProfileRequest(string Name, bool DryRun = false);
