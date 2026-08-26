using System.Text.Json;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.Core.Tests;

/// <summary>
/// The wire format is a privilege boundary, so it gets the same scrutiny as the engine:
/// anything that fails to round-trip is a request the service will misinterpret.
/// </summary>
public sealed class IpcProtocolTests
{
    // Goes through the same source-generated metadata the pipe uses, so a type someone forgot
    // to declare in IpcJsonContext fails here rather than in an ahead-of-time build.
    private static T RoundTrip<T>(T value) where T : class
        => (T)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(value, IpcJson.TypeInfo(typeof(T))),
            IpcJson.TypeInfo(typeof(T)))!;

    [Fact]
    public void A_request_with_a_typed_payload_round_trips()
    {
        var request = IpcRequest.Create(IpcCommands.Apply, new ChangeRequest
        {
            TweakIds = ["mmcss.system-responsiveness", "graphics.hags"],
            Options = new Dictionary<string, string> { ["priority"] = "High" },
            TargetProcessId = 4242,
            DryRun = true,
            Origin = "test",
        });

        var payload = RoundTrip(request).PayloadAs<ChangeRequest>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload.TweakIds.Count);
        Assert.Equal("High", payload.Options["priority"]);
        Assert.Equal(4242, payload.TargetProcessId);
        Assert.True(payload.DryRun);
        Assert.Equal("test", payload.Origin);
    }

    [Fact]
    public void A_request_with_no_payload_round_trips()
    {
        var request = IpcRequest.Create(IpcCommands.Ping);

        var restored = RoundTrip(request);

        Assert.Equal(IpcCommands.Ping, restored.Command);
        Assert.Null(restored.PayloadAs<ChangeRequest>());
    }

    [Fact]
    public void Enums_cross_the_wire_as_names_not_numbers()
    {
        // Numeric enums would silently re-map if anyone reordered the enum, and the whole
        // point of the protocol being readable is that a human can audit a request.
        var result = new ChangeResult("graphics.hags", Outcome.RolledBack, "nope", true);

        var json = JsonSerializer.Serialize(result, IpcJson.TypeInfo(typeof(ChangeResult)));

        Assert.Contains("RolledBack", json, StringComparison.Ordinal);
        Assert.Equal(Outcome.RolledBack, RoundTrip(result).Outcome);
    }

    [Fact]
    public void A_success_response_carries_its_result()
    {
        var response = IpcResponse.Success("abc", new PingResult(1, "0.1.0", 99, 9, 2));

        var restored = RoundTrip(response);
        var ping = restored.ResultAs<PingResult>();

        Assert.True(restored.Ok);
        Assert.Equal("abc", restored.Id);
        Assert.NotNull(ping);
        Assert.Equal(99, ping.ProcessId);
        Assert.Equal(2, ping.OutstandingChanges);
    }

    [Fact]
    public void A_failure_response_carries_the_message_and_no_result()
    {
        var restored = RoundTrip(IpcResponse.Failure("abc", "unknown tweak id"));

        Assert.False(restored.Ok);
        Assert.Equal("unknown tweak id", restored.Error);
        Assert.Null(restored.Result);
    }

    [Fact]
    public void Tweak_metadata_survives_the_round_trip()
    {
        var summary = new TweakSummary(
            "power.ultimate-performance", "Title", "Summary", "power",
            TweakScope.Machine, TweakLifetime.Persistent, Risk.Moderate, Evidence.Measured,
            RequiresReboot: false, RequiresElevation: true,
            Choices:
            [
                new TweakChoice
                {
                    Id = "level",
                    Title = "Level",
                    Description = "How hard to push it.",
                    DefaultOption = "balanced",
                    Options =
                    [
                        new TweakChoiceOption
                        {
                            Id = "balanced", Title = "Balanced", Description = "The safe one.",
                            Recommended = true,
                        },
                        new TweakChoiceOption
                        {
                            Id = "max", Title = "Maximum", Description = "The loud one.",
                        },
                    ],
                },
            ]);

        var restored = RoundTrip(summary);

        // The descriptions are the entire reason a choice exists; losing them on the wire would
        // leave the UI showing a dropdown of bare words.
        var choice = Assert.Single(restored.Choices);
        Assert.Equal("balanced", choice.DefaultOption);
        Assert.Equal(2, choice.Options.Count);
        Assert.Equal("The safe one.", choice.Options[0].Description);
        Assert.True(choice.Options[0].Recommended);

        Assert.Equal(Risk.Moderate, restored.Risk);
        Assert.Equal(Evidence.Measured, restored.Evidence);
        Assert.Equal(TweakScope.Machine, restored.Scope);
        Assert.True(restored.RequiresElevation);
    }

    [Fact]
    public void Request_ids_are_unique_so_replies_cannot_be_confused()
    {
        var ids = Enumerable.Range(0, 100)
            .Select(_ => IpcRequest.Create(IpcCommands.Ping).Id)
            .ToHashSet();

        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public void The_request_size_cap_is_small_enough_to_bound_allocations()
    {
        // A privileged listener must not let an unprivileged caller pick the allocation size.
        Assert.InRange(IpcContract.MaxRequestBytes, 1024, 4 * 1024 * 1024);
    }

    [Fact]
    public void A_plain_string_result_round_trips()
    {
        // A command is allowed to answer with a bare string rather than a record. This broke
        // once: the daemon serialized the string itself and then Success serialized the
        // resulting node a second time, which failed on a JsonNode type the contract has never
        // heard of. Nothing in the protocol currently does it, which is exactly why the guard
        // has to stay -- the next command that does will not repeat the investigation.
        var response = IpcResponse.Success("abc", "reverted 2 change(s)");

        var restored = RoundTrip(response);

        Assert.Equal("reverted 2 change(s)", restored.Result?.GetValue<string>());
    }

    [Fact]
    public void A_result_that_is_already_json_passes_through_untouched()
    {
        var node = System.Text.Json.Nodes.JsonValue.Create("already encoded");

        var response = IpcResponse.Success("abc", node);

        Assert.Equal("already encoded", RoundTrip(response).Result?.GetValue<string>());
    }

    [Fact]
    public void A_payload_type_outside_the_contract_is_refused_loudly()
    {
        // The alternative is reflection-based serialization that works everywhere except an
        // ahead-of-time build, which is the worst place to discover it.
        var error = Assert.Throws<InvalidOperationException>(
            () => IpcRequest.Create("apply", new Uri("https://example.invalid")));

        Assert.Contains("IpcJsonContext", error.Message, StringComparison.Ordinal);
    }

}
