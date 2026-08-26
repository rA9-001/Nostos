using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Declarative;

/// <summary>
/// Executes a <see cref="RegistryTweakDefinition"/>.
///
/// One class covers the whole declarative catalog, so every registry tweak in the project
/// gets identical capture, revert and verify semantics. There is no per-tweak revert code to
/// get wrong.
/// </summary>
public sealed class RegistryTweak : ITweak
{
    private readonly RegistryTweakDefinition _definition;

    public RegistryTweak(RegistryTweakDefinition definition)
    {
        _definition = definition;
        Metadata = definition.ToMetadata();
    }

    public TweakMetadata Metadata { get; }

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
    {
        if (_definition.MinBuild > 0 && SystemInfo.Build < _definition.MinBuild)
            return Task.FromResult(Applicability.No(
                $"needs Windows build {_definition.MinBuild} or later (this machine is {SystemInfo.Build})"));

        if (_definition.DesktopOnly && SystemInfo.HasBattery)
            return Task.FromResult(Applicability.No(
                "this tweak trades battery life for performance and is disabled on battery-powered machines"));

        return Task.FromResult(Applicability.Applicable);
    }

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        // "Applied" means "matches the option currently selected", not "matches some option".
        // A tweak sitting on Balanced when the user has picked Aggressive is not applied.
        var wanted = _definition.ValuesFor(context.Options);
        var parts = new List<string>(wanted.Count);
        var allMatch = true;

        foreach (var spec in wanted)
        {
            var (current, kind) = RegistryAccess.Read(spec.ToRef());
            var currentText = current is null ? "(not set)" : RegistryAccess.Encode(current, kind);
            var desiredText = RegistryAccess.Encode(spec.DecodedValue(), spec.Kind);

            if (!string.Equals(currentText, desiredText, StringComparison.OrdinalIgnoreCase))
                allMatch = false;

            // Compared on the encoded form, printed on the readable one. The two differ only
            // for unsigned bitmasks, and comparing on the display text would make the format
            // of a message part of the correctness of a read.
            var shownText = current is null ? "(not set)" : RegistryAccess.Describe(current, kind);
            parts.Add($"{(spec.Name.Length == 0 ? "(default)" : spec.Name)} = {shownText}");
        }

        var description = string.Join("; ", parts);
        if (_definition.Choices.Count > 0)
            description += $" [{_definition.DescribeSelections(context.Options)}]";

        return Task.FromResult(new TweakState(allMatch, description));
    }

    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        // Capture every value ANY option could write, not just the selected one. Applying
        // Balanced and then Aggressive without reverting in between would otherwise leave the
        // first option's values outside the snapshot, and revert would miss them.
        var captured = new JsonArray();
        foreach (var spec in _definition.AllReachableValues)
        {
            // Cast to JsonNode so this binds to JsonArray's IList<JsonNode?> Add rather than
            // the generic Add<T>, which serializes by reflection and cannot be AOT-compiled.
            // Capture already returns a JsonObject, so there is nothing to serialize.
            captured.Add((JsonNode?)RegistryAccess.Capture(spec.ToRef()));
        }

        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject { ["values"] = captured }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        foreach (var spec in _definition.ValuesFor(context.Options))
        {
            ct.ThrowIfCancellationRequested();
            RegistryAccess.Write(spec.ToRef(), spec.DecodedValue(), spec.Kind);
            context.Log.Debug($"{Metadata.Id}: set {spec.ToRef()} = {spec.Value}");
        }
        return Task.CompletedTask;
    }

    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        if (snapshot.Data["values"] is not JsonArray values)
            throw new InvalidDataException($"{Metadata.Id}: snapshot has no 'values' array.");

        // Restore every captured value, even if a later one throws, so a partial failure still
        // puts back as much as it can. The first error is rethrown once the loop is done.
        Exception? firstError = null;
        foreach (var node in values)
        {
            if (node is not JsonObject captured)
                continue;
            try
            {
                RegistryAccess.Restore(captured);
            }
            catch (Exception e)
            {
                firstError ??= e;
                context.Log.Error($"{Metadata.Id}: could not restore a value", e);
            }
        }

        if (firstError is not null)
            throw firstError;

        return Task.CompletedTask;
    }

    public async Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => (await ReadAsync(context, ct).ConfigureAwait(false)).IsApplied;
}
