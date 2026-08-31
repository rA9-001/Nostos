using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Journal;

namespace Nostos.Core.Tests;

public sealed class TweakEngineTests : IDisposable
{
    private readonly string _journalPath = Path.Combine(
        Path.GetTempPath(), "nostos-tests", Guid.NewGuid().ToString("n"), "journal.jsonl");

    private TweakEngine BuildEngine(params ITweak[] tweaks)
        => new(new TweakRegistry(tweaks), new JsonlJournal(_journalPath));

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_journalPath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Apply_changes_the_value_and_journals_the_prior_one()
    {
        var tweak = new FakeTweak();
        var engine = BuildEngine(tweak);

        var result = await engine.ApplyAsync(tweak.Metadata.Id);

        Assert.Equal(Outcome.Applied, result.Outcome);
        Assert.Equal("tweaked", tweak.MachineValue);

        var outstanding = await new JsonlJournal(_journalPath).GetOutstandingAsync();
        Assert.Equal("original", outstanding[tweak.Metadata.Id].Data["prior"]!.GetValue<string>());
    }

    [Fact]
    public async Task Revert_restores_the_captured_value_not_a_hardcoded_default()
    {
        // The machine started at a non-default value, which is the case that breaks
        // optimizers that "restore defaults" instead of restoring what was there.
        var tweak = new FakeTweak(initialValue: "oem-custom-value");
        var engine = BuildEngine(tweak);

        await engine.ApplyAsync(tweak.Metadata.Id);
        var result = await engine.RevertAsync(tweak.Metadata.Id);

        Assert.Equal(Outcome.Reverted, result.Outcome);
        Assert.Equal("oem-custom-value", tweak.MachineValue);
    }

    [Fact]
    public async Task Apply_that_throws_is_rolled_back_to_the_captured_value()
    {
        var tweak = new FakeTweak { ThrowOnApply = true };
        var engine = BuildEngine(tweak);

        var result = await engine.ApplyAsync(tweak.Metadata.Id);

        Assert.Equal(Outcome.RolledBack, result.Outcome);
        Assert.Equal("original", tweak.MachineValue);
        Assert.Equal(1, tweak.RevertCount);
    }

    [Fact]
    public async Task Apply_that_throws_and_fails_to_roll_back_reports_failure_honestly()
    {
        var tweak = new FakeTweak { ThrowOnApply = true, ThrowOnRevert = true };
        var engine = BuildEngine(tweak);

        var result = await engine.ApplyAsync(tweak.Metadata.Id);

        Assert.Equal(Outcome.Failed, result.Outcome);
        Assert.Contains("rollback failed", result.Message, StringComparison.OrdinalIgnoreCase);
        // The machine is in a known-bad state, and the journal must still point at the prior value.
        var outstanding = await new JsonlJournal(_journalPath).GetOutstandingAsync();
        Assert.True(outstanding.ContainsKey(tweak.Metadata.Id));
    }

    [Fact]
    public async Task Applying_twice_still_reverts_to_the_original_value()
    {
        var tweak = new FakeTweak();
        var engine = BuildEngine(tweak);

        await engine.ApplyAsync(tweak.Metadata.Id);
        tweak.MachineValue = "drifted";
        await engine.ApplyAsync(tweak.Metadata.Id);
        await engine.RevertAsync(tweak.Metadata.Id);

        Assert.Equal("original", tweak.MachineValue);
    }

    [Fact]
    public async Task Already_applied_tweaks_are_not_reapplied()
    {
        var tweak = new FakeTweak(initialValue: "tweaked");
        var engine = BuildEngine(tweak);

        var result = await engine.ApplyAsync(tweak.Metadata.Id);

        Assert.Equal(Outcome.AlreadyApplied, result.Outcome);
        Assert.Equal(0, tweak.ApplyCount);
    }

    [Fact]
    public async Task Dry_run_changes_nothing_and_writes_no_journal_entry()
    {
        var tweak = new FakeTweak();
        var engine = BuildEngine(tweak);

        var result = await engine.ApplyAsync(
            tweak.Metadata.Id, new TweakContext { DryRun = true });

        Assert.Equal(Outcome.Skipped, result.Outcome);
        Assert.Equal("original", tweak.MachineValue);
        Assert.Empty(await new JsonlJournal(_journalPath).ReadAllAsync());
    }

    [Fact]
    public async Task Unverified_applies_are_reported_but_stay_revertible()
    {
        var tweak = new FakeTweak { VerifyResult = false };
        var engine = BuildEngine(tweak);

        var result = await engine.ApplyAsync(tweak.Metadata.Id);

        Assert.Equal(Outcome.Unverified, result.Outcome);
        var outstanding = await new JsonlJournal(_journalPath).GetOutstandingAsync();
        Assert.True(outstanding.ContainsKey(tweak.Metadata.Id));
    }

    [Fact]
    public async Task Revert_all_undoes_everything_in_reverse_order()
    {
        var first = new FakeTweak("test.first");
        var second = new FakeTweak("test.second");
        var engine = BuildEngine(first, second);

        await engine.ApplyManyAsync(
            [new TweakSelection("test.first"), new TweakSelection("test.second")]);
        var results = await engine.RevertAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("test.second", results[0].TweakId);
        Assert.All(results, r => Assert.Equal(Outcome.Reverted, r.Outcome));
        Assert.Equal("original", first.MachineValue);
        Assert.Equal("original", second.MachineValue);
    }

    [Fact]
    public async Task A_batch_says_where_it_has_got_to_as_it_goes()
    {
        // So a window can show a profile being worked through rather than a spinner over a
        // frozen list. Reported from inside the loop that does the work, which is the whole
        // point: the alternative -- animating through the list at a plausible rate while the
        // real batch runs elsewhere -- puts something on screen that is not true.
        var engine = BuildEngine(new FakeTweak("test.first"), new FakeTweak("test.second"));
        var reports = new List<BatchProgress>();

        await engine.ApplyManyAsync(
            [new TweakSelection("test.first"), new TweakSelection("test.second")],
            onProgress: p =>
            {
                reports.Add(p);
                return Task.CompletedTask;
            });

        // Twice per tweak: once on the way in with no outcome, once on the way out with one.
        Assert.Equal(
            [("test.first", null), ("test.first", Outcome.Applied),
             ("test.second", null), ("test.second", Outcome.Applied)],
            reports.Select(r => (r.TweakId, r.Outcome)));

        Assert.All(reports, r => Assert.Equal(2, r.Total));
        Assert.Equal([1, 1, 2, 2], reports.Select(r => r.Index));
    }

    [Fact]
    public async Task The_report_stops_on_the_tweak_that_stops()
    {
        // A progress display whose last word is the tweak that failed is worth having. One that
        // carries on to "42 of 42" because it was counting on a timer is worse than none.
        var second = new FakeTweak("test.second") { ThrowOnApply = true };
        var engine = BuildEngine(new FakeTweak("test.first"), second);
        var reports = new List<BatchProgress>();

        await engine.ApplyManyAsync(
            [new TweakSelection("test.first"), new TweakSelection("test.second")],
            onProgress: p =>
            {
                reports.Add(p);
                return Task.CompletedTask;
            });

        Assert.Equal("test.second", reports[^1].TweakId);
        Assert.NotEqual(Outcome.Applied, reports[^1].Outcome);
    }

    [Fact]
    public async Task A_batch_refused_as_a_whole_reports_nothing_rather_than_a_phantom_run()
    {
        // Conflicts and the safety gate refuse before the loop starts, so nothing was worked
        // on and nothing should appear to have been. The refusal comes back in the results.
        var first = new FakeTweak("test.first")
        {
            Metadata = new FakeTweak("test.first").Metadata with { ConflictsWith = ["test.second"] },
        };
        var engine = BuildEngine(first, new FakeTweak("test.second"));
        var reports = new List<BatchProgress>();

        var results = await engine.ApplyManyAsync(
            [new TweakSelection("test.first"), new TweakSelection("test.second")],
            onProgress: p =>
            {
                reports.Add(p);
                return Task.CompletedTask;
            });

        Assert.Empty(reports);
        Assert.All(results, r => Assert.Equal(Outcome.Skipped, r.Outcome));
    }

    [Fact]
    public async Task Conflicting_tweaks_are_refused_as_a_batch_without_applying_either()
    {
        var first = new FakeTweak("test.first")
        {
            Metadata = new FakeTweak("test.first").Metadata with { ConflictsWith = ["test.second"] },
        };
        var second = new FakeTweak("test.second");
        var engine = BuildEngine(first, second);

        var results = await engine.ApplyManyAsync(
            [new TweakSelection("test.first"), new TweakSelection("test.second")]);

        Assert.All(results, r => Assert.Equal(Outcome.Skipped, r.Outcome));
        Assert.Equal("original", first.MachineValue);
        Assert.Equal("original", second.MachineValue);
    }

    [Fact]
    public async Task Unelevated_machine_scope_tweaks_are_skipped_not_attempted()
    {
        var tweak = new FakeTweak();
        var elevated = new FakeTweak("test.elevated")
        {
            Metadata = new FakeTweak("test.elevated").Metadata with { RequiresElevation = true },
        };
        var engine = new TweakEngine(
            new TweakRegistry([tweak, elevated]),
            new JsonlJournal(_journalPath),
            new NotElevated());

        var results = await engine.ApplyManyAsync(
            [new TweakSelection("test.tweak"), new TweakSelection("test.elevated")]);

        Assert.Equal(Outcome.Applied, results[0].Outcome);
        Assert.Equal(Outcome.Skipped, results[1].Outcome);
        Assert.Equal(0, elevated.ApplyCount);
    }

    [Fact]
    public async Task Reconcile_reapplies_persistent_tweaks_that_drifted()
    {
        // This is the Windows Update case: the key we set gets reset behind our back.
        var tweak = new FakeTweak();
        var engine = BuildEngine(tweak);

        await engine.ApplyAsync(tweak.Metadata.Id);
        tweak.MachineValue = "reset-by-windows-update";

        var results = await engine.ReconcileAsync();

        Assert.Single(results);
        Assert.Equal("tweaked", tweak.MachineValue);
    }

    [Fact]
    public async Task Reconcile_ignores_session_scoped_tweaks()
    {
        var tweak = new FakeTweak(lifetime: TweakLifetime.SessionOnly);
        var engine = BuildEngine(tweak);

        await engine.ApplyAsync(tweak.Metadata.Id);
        tweak.MachineValue = "process restarted";

        Assert.Empty(await engine.ReconcileAsync());
    }

    private sealed class NotElevated : IPrivilegeCheck
    {
        public bool IsElevated => false;
    }
}
