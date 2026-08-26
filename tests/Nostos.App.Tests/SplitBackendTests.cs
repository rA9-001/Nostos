using Nostos.App.Backends;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// Routing rules for the composite backend.
///
/// The bug these exist for was silent and had nothing to announce it: the service runs as
/// LocalSystem, so it reads and writes SYSTEM's user hive. A user-scoped tweak the user had
/// applied read back as "off", and an apply that should have been a no-op looked like real work
/// while changing a hive nobody was looking at.
/// </summary>
public sealed class SplitBackendTests
{
    /// <summary>Stands in for the in-process engine, with a catalog the test controls.</summary>
    private sealed class FakeLocalBackend : FakeBackend, ILocalBackend
    {
        public Dictionary<string, TweakScope> Scopes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<TweakSelection>> Profiles { get; }
            = new(StringComparer.OrdinalIgnoreCase);

        public TweakScope? ScopeOf(string tweakId)
            => Scopes.TryGetValue(tweakId, out var scope) ? scope : null;

        public IReadOnlyList<TweakSelection> ProfileSelections(string name)
            => Profiles.TryGetValue(name, out var selections)
                ? selections
                : throw new KeyNotFoundException(name);
    }

    private static (FakeBackend Service, FakeLocalBackend Local, SplitBackend Split) Build()
    {
        var service = new FakeBackend();
        var local = new FakeLocalBackend
        {
            Scopes = { ["a.machine"] = TweakScope.Machine, ["a.user"] = TweakScope.User },
        };

        return (service, local, new SplitBackend(service, local));
    }

    [Fact]
    public async Task Machine_scoped_work_goes_to_the_service()
    {
        var (service, local, split) = Build();

        await split.ApplyAsync("a.machine");

        Assert.Equal(["a.machine"], service.Applied);
        Assert.Empty(local.Applied);
    }

    [Fact]
    public async Task User_scoped_work_never_reaches_the_service()
    {
        var (service, local, split) = Build();

        await split.ApplyAsync("a.user");

        // Over the pipe this would have written SYSTEM's hive instead of the user's, which is
        // the entire reason this class exists.
        Assert.Empty(service.Applied);
        Assert.Equal(["a.user"], local.Applied);
    }

    [Fact]
    public async Task Reverting_a_user_scoped_tweak_is_also_kept_local()
    {
        var (service, local, split) = Build();

        await split.RevertAsync("a.user");

        Assert.Empty(service.Applied);
        Assert.Contains("a.user", local.Reverted);
    }

    [Fact]
    public async Task An_unknown_tweak_falls_through_to_the_service()
    {
        // Unknown means the local catalog does not have it, which is a build mismatch rather
        // than a scope decision. The service owns the authoritative catalog, so it answers.
        var (service, _, split) = Build();

        await split.ApplyAsync("a.unknown");

        Assert.Equal(["a.unknown"], service.Applied);
    }

    [Fact]
    public async Task The_catalog_listing_re_reads_user_scoped_rows_locally()
    {
        var (service, local, split) = Build();

        // What the service would report: the tweak looks unset, because SYSTEM's hive is.
        service.Statuses.Add(FakeBackend.Tweak("a.machine", applied: true));
        service.Statuses.Add(FakeBackend.Tweak("a.user", applied: false));

        // What is actually true for this user.
        local.Statuses.Add(FakeBackend.Tweak("a.user", applied: true));

        var statuses = await split.GetStatusAsync();

        Assert.True(statuses.Single(s => s.Tweak.Id == "a.machine").IsApplied);
        Assert.True(statuses.Single(s => s.Tweak.Id == "a.user").IsApplied);

        // And the machine-scoped row was taken from the service without a second read.
        Assert.DoesNotContain(service.StatusReads, r => r.TweakId == "a.machine");
    }

    [Fact]
    public async Task A_profile_is_split_across_both_halves()
    {
        var (service, local, split) = Build();
        local.Profiles["mixed"] =
        [
            new TweakSelection("a.machine"),
            new TweakSelection("a.user"),
        ];

        await split.ApplyProfileAsync("mixed");

        // Sending the whole profile to the service would have skipped the user-scoped half
        // with an explanation, which for the shipped profiles is most of the entries.
        Assert.Equal(["a.machine"], service.Applied);
        Assert.Equal(["a.user"], local.Applied);
    }
}
