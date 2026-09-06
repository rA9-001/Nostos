using Nostos.Win32.Services;

namespace Nostos.Win32.Cleanup;

/// <summary>
/// Why a target cannot be emptied by this process, when that is knowable without trying.
///
/// A value rather than a sentence. The sentence has to be written in the reader's language and
/// has to say what to do about it, and neither of those is a decision this layer can make: it
/// runs inside a service with no user as often as inside the window. What it knows is the fact.
/// </summary>
public enum CleanupBlock
{
    /// <summary>Nothing in the way.</summary>
    None,

    /// <summary>Outside this user's profile, and this process is not elevated.</summary>
    NeedsAdmin,
}

/// <summary>What one target holds right now.</summary>
/// <param name="Bytes">Total size of the files that would be removed.</param>
/// <param name="Files">How many of them there are.</param>
/// <param name="Held">
/// Files that match but are older than the age rule allows, or that could not be measured. Shown
/// so a number that looks too small has an explanation next to it.
/// </param>
/// <param name="Blocked">
/// What stands in the way, or None when nothing does. Set for a machine-wide folder while the
/// app is not elevated -- the size is still measured, because "you would get 2 GB back if you
/// ran this elevated" is worth knowing.
/// </param>
public sealed record CleanupFinding(
    CleanupTarget Target,
    long Bytes,
    int Files,
    int Held = 0,
    CleanupBlock Blocked = CleanupBlock.None,
    bool Exists = true)
{
    public bool HasAnything => Files > 0;

    /// <summary>Known in advance, and said in advance: the button stays and explains itself.</summary>
    public bool NeedsAdmin => Blocked == CleanupBlock.NeedsAdmin;

    public bool CanClean => Blocked == CleanupBlock.None && Files > 0;
}

/// <summary>What actually happened when a target was emptied.</summary>
/// <param name="Blocked">
/// Set when the sweep was refused before it started. Kept apart from <paramref name="Problems"/>
/// so the window can say what to do about it rather than repeat a sentence written here.
/// </param>
public sealed record CleanupResult(
    string Id,
    long BytesFreed,
    int FilesRemoved,
    int Skipped,
    IReadOnlyList<string> Problems,
    CleanupBlock Blocked = CleanupBlock.None)
{
    public bool NeedsAdmin => Blocked == CleanupBlock.NeedsAdmin;
}

/// <summary>
/// Measures and empties the places in <see cref="CleanupTargets"/>.
///
/// Everything here is deliberately dull. The interesting decisions -- which places, and why each
/// is safe -- were made in the catalogue; what is left is doing it without surprises:
///
/// <list type="bullet">
/// <item>Never the folder itself, only what is inside it. Programs expect their cache directory
/// to exist and several will not recreate it.</item>
/// <item>Never across a junction or symlink. A reparse point inside Temp can point anywhere on
/// the machine, and following one turns "empty a cache" into "delete somebody's documents".
/// Every file is also checked to be under the target after its full path is resolved.</item>
/// <item>Never a file in use. That is the normal case in Temp, not an error: it is skipped,
/// counted, and reported as a number rather than as a failure.</item>
/// </list>
/// </summary>
public static class Cleanup
{
    /// <summary>Measures every target. Reads only; changes nothing.</summary>
    public static IReadOnlyList<CleanupFinding> Scan(bool elevated)
        => [.. CleanupTargets.All.Select(t => Measure(t, elevated))];

    /// <summary>Measures one target.</summary>
    public static CleanupFinding Measure(CleanupTarget target, bool elevated)
    {
        var blocked = target.NeedsElevation && !elevated ? CleanupBlock.NeedsAdmin : CleanupBlock.None;

        if (!Directory.Exists(target.Path))
            return new CleanupFinding(target, 0, 0, 0, blocked, Exists: false);

        long bytes = 0;
        var files = 0;
        var held = 0;
        var cutoff = target.MinimumAge is { } age ? DateTime.UtcNow - age : (DateTime?)null;

        foreach (var file in Files(target))
        {
            try
            {
                if (cutoff is { } before && file.LastWriteTimeUtc > before)
                {
                    held++;
                    continue;
                }

                bytes += file.Length;
                files++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                held++;
            }
        }

        return new CleanupFinding(target, bytes, files, held, blocked);
    }

    /// <summary>
    /// Empties one target, and reports what it could not take.
    ///
    /// Deleted rather than recycled, which is the one place in this program that does that. The
    /// catalogue earns it: everything on the list regenerates, so there is nothing to undo, and
    /// recycling would free no space until the bin was emptied -- the same bin that holds the
    /// undo for the uninstaller.
    /// </summary>
    public static CleanupResult Clean(
        CleanupTarget target,
        bool elevated,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (target.NeedsElevation && !elevated)
            return new CleanupResult(target.Id, 0, 0, 0, [], CleanupBlock.NeedsAdmin);

        if (!Directory.Exists(target.Path))
            return new CleanupResult(target.Id, 0, 0, 0, []);

        long freed = 0;
        var removed = 0;
        var skipped = 0;
        var problems = new List<string>();
        var cutoff = target.MinimumAge is { } age ? DateTime.UtcNow - age : (DateTime?)null;

        foreach (var file in Files(target))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (cutoff is { } before && file.LastWriteTimeUtc > before)
                {
                    skipped++;
                    continue;
                }

                var size = file.Length;
                onProgress?.Invoke(file.FullName);

                // Read-only is not a claim about importance here: installers leave read-only
                // files in Temp all the time, and this is the file's own directory being emptied.
                if (file.IsReadOnly)
                    file.IsReadOnly = false;

                file.Delete();

                freed += size;
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // In use, or not ours. The normal case in Temp, and not worth a line each.
                skipped++;
            }
        }

        Prune(target, problems, ct);

        return new CleanupResult(target.Id, freed, removed, skipped, problems);
    }

    /// <summary>
    /// The files belonging to a target, never crossing a link out of it.
    ///
    /// Walked by hand rather than with <c>SearchOption.AllDirectories</c> for one reason: that
    /// overload follows junctions and symbolic links. A reparse point under Temp can point at
    /// any directory on the machine, and an installer that left one behind would turn emptying a
    /// cache into deleting whatever it pointed at.
    /// </summary>
    private static IEnumerable<FileInfo> Files(CleanupTarget target)
    {
        var root = Full(target.Path);
        if (root is null)
            yield break;

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!IsLink(child) && Inside(root, child))
                    pending.Push(child);
            }

            IEnumerable<string> matches;
            try
            {
                matches = Directory.EnumerateFiles(directory, target.Pattern);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in matches)
            {
                if (IsLink(path) || !Inside(root, path))
                    continue;

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    continue;
                }

                yield return info;
            }
        }
    }

    /// <summary>
    /// Removes the empty directories left behind, but never the target itself.
    ///
    /// A cache directory that disappears is a bug in whatever owned it, not a tidier machine:
    /// several programs create theirs once at install time and assume it from then on.
    /// </summary>
    private static void Prune(CleanupTarget target, List<string> problems, CancellationToken ct)
    {
        var root = Full(target.Path);
        if (root is null || target.Pattern != "*")
            return;

        // Deepest first, so a directory whose children have just gone is empty when reached.
        foreach (var directory in Directories(root).OrderByDescending(d => d.Length))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Something is still in it, or it is held open. Neither is worth reporting: the
                // files are what the caller asked about, and they have already been counted.
            }
        }
    }

    private static IEnumerable<string> Directories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (IsLink(child) || !Inside(root, child))
                    continue;

                pending.Push(child);
                yield return child;
            }
        }
    }

    /// <summary>A junction, symbolic link or mount point.</summary>
    public static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unreadable is treated as a link: both mean "do not walk into it".
            return true;
        }
    }

    /// <summary>
    /// Whether a resolved path is genuinely under the root.
    ///
    /// The backstop for the link check rather than a duplicate of it. Path resolution is where
    /// this kind of code goes wrong, and a delete that escapes its root is the one failure this
    /// feature must not have.
    /// </summary>
    public static bool Inside(string root, string path)
    {
        var full = Full(path);
        var top = Full(root);

        if (full is null || top is null)
            return false;

        return full.Equals(top, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Full(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Whether this process could touch machine-wide locations.</summary>
    public static bool IsElevated => WindowsPrivilegeCheck.Instance.IsElevated;
}
