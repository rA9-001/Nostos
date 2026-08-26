namespace Nostos.Core.Tests;

/// <summary>
/// Migration behaviour for machines that ran a build with the auto-revert watchdog in it.
///
/// The watchdog is gone: nothing reverts a change unless a person asks. Its marker file is not
/// gone from the machines that already have one, and a file called "pending.json" living beside
/// the journal is a standing invitation to conclude that something is still queued to happen.
/// </summary>
public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "nostos-tests", Guid.NewGuid().ToString("n"));

    public AppPathsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Marker => Path.Combine(_root, "pending.json");

    [Fact]
    public void A_leftover_watchdog_marker_is_removed()
    {
        File.WriteAllText(Marker, """{"Id":"old","TweakIds":["graphics.hags"]}""");

        AppPaths.RemoveLegacyPendingMarker(_root);

        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void Removing_a_marker_that_is_not_there_is_not_an_error()
    {
        // The overwhelmingly common case: a clean install, every launch after the first, and
        // every portable copy. Startup calls this unconditionally, so it has to be silent.
        AppPaths.RemoveLegacyPendingMarker(_root);

        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void A_marker_that_cannot_be_deleted_does_not_take_startup_down()
    {
        // Held open by another process is the realistic version of this. A file nothing reads
        // any more must never be the reason the app fails to start.
        using var held = new FileStream(
            Marker, FileMode.Create, FileAccess.Write, FileShare.None);

        AppPaths.RemoveLegacyPendingMarker(_root);
    }

    [Fact]
    public void Nothing_else_in_the_data_folder_is_touched()
    {
        var journal = Path.Combine(_root, "journal.jsonl");
        File.WriteAllText(journal, "{}");

        AppPaths.RemoveLegacyPendingMarker(_root);

        Assert.True(File.Exists(journal));
    }
}
