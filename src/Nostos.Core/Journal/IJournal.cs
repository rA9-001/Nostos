using Nostos.Core.Abstractions;

namespace Nostos.Core.Journal;

public interface IJournal
{
    Task AppendAsync(JournalEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntry>> ReadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Tweaks that have been applied and not yet reverted, with the snapshot needed to undo them.
    /// Replays the whole log rather than keeping a mutable "current state" file, so a torn write
    /// costs at most the last entry instead of the ability to restore the machine.
    /// </summary>
    Task<IReadOnlyDictionary<string, TweakSnapshot>> GetOutstandingAsync(CancellationToken ct = default);
}
