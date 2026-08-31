using Nostos.Core.Abstractions;
using Nostos.Core.Journal;

namespace Nostos.Core.Tests;

/// <summary>
/// Startup switches in the change log.
///
/// These lines are recorded so the History tab is a complete account of what this program did to
/// the machine. They are deliberately <see cref="JournalAction.ApplyCommitted"/> with no
/// preceding intent, which is what keeps them out of `nos revert --all`: a revert that turns
/// Razer Synapse back on months later, as a side effect of undoing something unrelated, is worse
/// than either recording it or not.
/// </summary>
public sealed class StartupJournalTests
{
    private sealed class RecordingJournal : IJournal
    {
        public List<JournalEntry> Entries { get; } = [];

        public Task AppendAsync(JournalEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JournalEntry>> ReadAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JournalEntry>>(Entries);

        public async Task<IReadOnlyDictionary<string, TweakSnapshot>> GetOutstandingAsync(
            CancellationToken ct = default)
        {
            var outstanding = new Dictionary<string, TweakSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in await ReadAllAsync(ct))
            {
                switch (entry.Action)
                {
                    case JournalAction.ApplyIntent when entry.Snapshot is not null:
                        outstanding.TryAdd(entry.TweakId, entry.Snapshot);
                        break;
                    case JournalAction.RevertCommitted:
                        outstanding.Remove(entry.TweakId);
                        break;
                }
            }

            return outstanding;
        }
    }

    private static async Task<JournalEntry> RecordedAsync(bool enabled)
    {
        var journal = new RecordingJournal();
        await StartupJournal.RecordAsync(journal, "user-run:Steam", "Steam", enabled);
        return Assert.Single(journal.Entries);
    }

    [Fact]
    public async Task A_switch_is_recorded_as_a_committed_change()
    {
        var entry = await RecordedAsync(enabled: false);

        Assert.Equal(JournalAction.ApplyCommitted, entry.Action);
        Assert.Equal("startup:user-run:Steam", entry.TweakId);
        Assert.Equal("startup", entry.Origin);
        Assert.Equal("Steam → off", entry.Detail);
    }

    [Fact]
    public async Task A_switch_never_becomes_something_revert_all_would_undo()
    {
        // The whole reason these are committed-only. The outstanding set is built from intents
        // that carry a snapshot, so a line with neither is visible in the history and is never
        // something `revert --all` goes looking for.
        var journal = new RecordingJournal();

        await StartupJournal.RecordAsync(journal, "user-run:Steam", "Steam", enabled: false);
        await StartupJournal.RecordAsync(journal, "machine-run:Portmaster", "Portmaster", enabled: false);

        Assert.Empty(await journal.GetOutstandingAsync());
    }

    [Fact]
    public async Task A_switch_does_not_capture_a_snapshot_because_there_is_nothing_to_capture()
    {
        // Unlike a registry value, the prior state is one bit and it is visible in Task Manager.
        // A snapshot would be a record of something that needs no record.
        Assert.Null((await RecordedAsync(enabled: true)).Snapshot);
    }

    [Theory]
    [InlineData("startup:user-run:Steam", true)]
    [InlineData("STARTUP:machine-run:Portmaster", true)]
    [InlineData("mmcss.system-responsiveness", false)]
    [InlineData("process.persistent-priority", false)]
    public void A_startup_line_is_told_apart_from_a_tweak_by_its_id(string id, bool owned)
        => Assert.Equal(owned, StartupJournal.Owns(id));

    [Fact]
    public void The_program_name_is_recovered_from_the_line_for_the_headline()
    {
        // The name comes out of the detail rather than the id, because an id is a location --
        // "user-run:Steam" -- and the reader wants the program.
        Assert.Equal("Steam", StartupJournal.NameOf("startup:user-run:Steam", "Steam → off"));
        Assert.Equal("EA Desktop", StartupJournal.NameOf("startup:user-run:EADM", "EA Desktop → on"));
    }

    [Fact]
    public void A_line_with_no_detail_still_names_something_rather_than_nothing()
    {
        // Defensive: a hand-edited journal, or one written by a future build. Falling back to the
        // id keeps the row readable, which is the same bargain the tweak rows make for a tweak
        // that has since been removed from the catalog.
        Assert.Equal("user-run:Steam", StartupJournal.NameOf("startup:user-run:Steam", null));
        Assert.Equal("whatever", StartupJournal.NameOf("whatever", ""));
    }

    [Theory]
    [InlineData("Steam → on", true)]
    [InlineData("Steam → off", false)]
    [InlineData(null, false)]
    public void The_direction_of_the_switch_is_readable_back_off_the_line(string? detail, bool enabled)
        => Assert.Equal(enabled, StartupJournal.WasEnabled(detail));
}
