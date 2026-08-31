using Nostos.Win32.Removal;

namespace Nostos.App.Tests;

/// <summary>
/// The file half of removal, against real directories.
///
/// Lives in the app's test project because that is the one that references Nostos.Win32. What
/// is tested here is narrow on purpose: deleting a tree is not interesting, but deleting a tree
/// that has a locked file in it is, because that is the case the uninstaller has to survive
/// without either throwing or claiming success.
/// </summary>
public sealed class SystemRemovalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "nostos-tests", Guid.NewGuid().ToString("n"));

    public SystemRemovalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_whole_tree_goes_and_reports_nothing_left()
    {
        Write("journal.jsonl");
        Write(@"logs\service-20260826.log");
        Write(@"profiles\balanced.json");

        var leftovers = new List<string>();
        SystemRemoval.DeleteTree(_root, leftovers);

        Assert.False(Directory.Exists(_root));
        Assert.Empty(leftovers);
    }

    [Fact]
    public void Deleting_a_folder_that_is_already_gone_is_silent()
    {
        var missing = Path.Combine(_root, "never-existed");
        var leftovers = new List<string>();

        SystemRemoval.DeleteTree(missing, leftovers);

        Assert.Empty(leftovers);
    }

    [Fact]
    public void A_locked_file_is_named_rather_than_thrown()
    {
        // What actually happens in a portable copy: the renderer is loaded out of data\runtime
        // while the app that is deleting the folder is still running. Everything else must
        // still go, and the user has to be told which file is left.
        Write("journal.jsonl");
        var locked = Write(@"runtime\libSkiaSharp.dll");

        using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var leftovers = new List<string>();
        SystemRemoval.DeleteTree(_root, leftovers);

        Assert.Equal([locked], leftovers);
        Assert.False(File.Exists(Path.Combine(_root, "journal.jsonl")));
    }

    private static RemovalTargets Targets(bool singleFile, bool portable) => new(
        ServiceInstalled: false,
        DataRoot: portable ? @"C:\Users\Sam\Downloads\data" : @"C:\ProgramData\Nostos",
        IsPortable: portable,
        LocalState: null,
        InstallDirectory: @"C:\Users\Sam\Downloads",
        ExecutablePath: @"C:\Users\Sam\Downloads\Nostos.exe",
        SingleFile: singleFile,
        Helper: null);

    [Fact]
    public void A_one_file_copy_asks_you_to_delete_the_exe_not_the_folder_it_sits_in()
    {
        // The folder a single-file copy runs from is usually Downloads. Telling somebody to
        // delete it, to finish uninstalling a 30 MB executable, would be the single most
        // destructive sentence in the program.
        var targets = Targets(singleFile: true, portable: true);

        Assert.Equal([@"C:\Users\Sam\Downloads\Nostos.exe", @"C:\Users\Sam\Downloads\data"],
            targets.DeleteByHand);
    }

    [Fact]
    public void A_folder_install_asks_you_to_delete_the_folder()
    {
        var targets = Targets(singleFile: false, portable: false);

        Assert.Equal([@"C:\Users\Sam\Downloads"], targets.DeleteByHand);
    }

    [Fact]
    public void A_one_file_copy_that_stored_its_data_elsewhere_only_names_the_exe()
    {
        // Its data folder is under %ProgramData%, which removal has already deleted.
        var targets = Targets(singleFile: true, portable: false);

        Assert.Equal([@"C:\Users\Sam\Downloads\Nostos.exe"], targets.DeleteByHand);
    }

    [Theory]
    [InlineData(@"C:\Games\Nostos\data\runtime\x.dll", @"C:\Games\Nostos", true)]
    [InlineData(@"C:\Games\Nostos\Nostos.exe", @"C:\Games\Nostos\", true)]
    [InlineData(@"C:\ProgramData\Nostos\journal.jsonl", @"C:\Games\Nostos", false)]
    // A prefix match on the string alone would call this one true, and the leftovers report
    // would then quietly drop a file that is not inside the folder the user is deleting.
    [InlineData(@"C:\Games\Nostos-old\Nostos.exe", @"C:\Games\Nostos", false)]
    public void Whether_a_path_is_inside_a_folder_is_decided_on_path_boundaries(
        string path, string directory, bool inside)
    {
        Assert.Equal(inside, SystemRemoval.IsInside(path, directory));
    }
}
