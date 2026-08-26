using Nostos.Core.Abstractions;
using Nostos.Core.Journal;

namespace Nostos.Core.Tests;

public sealed class JournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nostos-tests", Guid.NewGuid().ToString("n"));

    private string JournalPath => Path.Combine(_directory, "journal.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static JournalEntry Entry(string tweakId, JournalAction action, string? prior = null)
        => JournalEntry.Create(
            tweakId, action, TweakContext.Default, "test",
            snapshot: prior is null
                ? null
                : TweakSnapshot.Create(tweakId, new System.Text.Json.Nodes.JsonObject { ["prior"] = prior }));

    [Fact]
    public async Task A_torn_final_line_does_not_lose_the_rest_of_the_log()
    {
        var journal = new JsonlJournal(JournalPath);
        await journal.AppendAsync(Entry("a", JournalAction.ApplyIntent, "1"));
        await journal.AppendAsync(Entry("b", JournalAction.ApplyIntent, "2"));

        // Simulate a power loss mid-write.
        await File.AppendAllTextAsync(JournalPath, "{\"EntryId\":\"trunc");

        var entries = await new JsonlJournal(JournalPath).ReadAllAsync();

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task An_apply_that_failed_still_counts_as_outstanding()
    {
        // A failed apply may have changed something before it threw, so the prior value must
        // stay available to revert.
        var journal = new JsonlJournal(JournalPath);
        await journal.AppendAsync(Entry("a", JournalAction.ApplyIntent, "1"));
        await journal.AppendAsync(Entry("a", JournalAction.ApplyFailed));

        Assert.True((await journal.GetOutstandingAsync()).ContainsKey("a"));
    }

    [Fact]
    public async Task A_failed_revert_leaves_the_tweak_outstanding()
    {
        var journal = new JsonlJournal(JournalPath);
        await journal.AppendAsync(Entry("a", JournalAction.ApplyIntent, "1"));
        await journal.AppendAsync(Entry("a", JournalAction.RevertFailed));

        Assert.True((await journal.GetOutstandingAsync()).ContainsKey("a"));
    }

    [Fact]
    public async Task A_committed_revert_clears_the_tweak()
    {
        var journal = new JsonlJournal(JournalPath);
        await journal.AppendAsync(Entry("a", JournalAction.ApplyIntent, "1"));
        await journal.AppendAsync(Entry("a", JournalAction.RevertCommitted));

        Assert.Empty(await journal.GetOutstandingAsync());
    }

    [Fact]
    public async Task Reapplying_after_a_revert_captures_a_fresh_snapshot()
    {
        var journal = new JsonlJournal(JournalPath);
        await journal.AppendAsync(Entry("a", JournalAction.ApplyIntent, "first"));
        await journal.AppendAsync(Entry("a", JournalAction.RevertCommitted));
        await journal.AppendAsync(Entry("a", JournalAction.ApplyIntent, "second"));

        var outstanding = await journal.GetOutstandingAsync();

        Assert.Equal("second", outstanding["a"].Data["prior"]!.GetValue<string>());
    }

    [Fact]
    public async Task Concurrent_appends_do_not_interleave()
    {
        var journal = new JsonlJournal(JournalPath);

        await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(i => journal.AppendAsync(Entry($"tweak{i}", JournalAction.ApplyIntent, i.ToString()))));

        Assert.Equal(50, (await journal.ReadAllAsync()).Count);
    }
}
