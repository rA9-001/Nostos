using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nostos.App.Localization;
using Nostos.Core.Localization;
using Nostos.Win32.Apps;
using Nostos.Win32.Services;

namespace Nostos.App.ViewModels;

/// <summary>One claim the registration makes, and whether it is still true.</summary>
public sealed record EvidenceRow(string Label, string Path, bool Present, string Note);

/// <summary>
/// One thing that can be taken away on its own.
/// </summary>
/// <param name="Owner">
/// The row it belongs to, carried so the button beside it can name both the program and the
/// path. The pairing is what the removal is checked against, so it travels together.
/// </param>
/// <param name="NeedsAdmin">
/// True when this one is out of reach without administrator rights. Read from the machine with
/// the rest of the panel, so the row can say so instead of the button finding out.
/// </param>
public sealed record LeftoverRow(
    AppItemViewModel Owner,
    string Path,
    string SizeText,
    string KindText,
    bool HoldsUserData,
    string UserDataNote,
    bool IsRegistration,
    bool NeedsAdmin = false);

/// <summary>Where a row has got to while it is being removed.</summary>
public enum RemovalRowState
{
    Idle,
    Uninstalling,
    Scanning,
    Sweeping,
    Removed,
    Failed,
}

/// <summary>
/// One installed program.
///
/// The row shows the name, the publisher, where it came from and roughly how big it is, because
/// those four together are what somebody uses to decide whether they still want it. The icon is
/// doing most of the recognising, exactly as on the Startup tab.
/// </summary>
public sealed class AppItemViewModel : ObservableObject
{
    private readonly Func<AppItemViewModel, Task> _remove;
    private RemovalRowState _state;
    private string? _detail;

    public AppItemViewModel(InstalledApp app, Func<AppItemViewModel, Task> remove)
        : this(app, remove, ReadIcon(app))
    {
    }

    /// <summary>
    /// Builds a row from artwork that has already been read.
    ///
    /// The split exists because the two halves belong on different threads. Finding a program's
    /// icon means searching the registry, probing package folders and calling into the shell,
    /// and doing that for a hundred programs measured 81ms of a window that could not repaint.
    /// Turning the result into a bitmap is a memory copy, and it has to happen on the UI thread:
    /// a WriteableBitmap is made through the render interface, which is not there on a worker.
    ///
    /// <para>So the caller reads on the pool with <see cref="ReadIcon"/> and constructs here.
    /// The single-argument constructor does both, which is what a test wants.</para>
    /// </summary>
    public AppItemViewModel(InstalledApp app, Func<AppItemViewModel, Task> remove, IconSource icon)
    {
        App = app;
        _remove = remove;
        Icon = Draw(icon);
    }

    public InstalledApp App { get; }

    public string Id => App.Id;

    public string Name => App.Name;

    public Bitmap? Icon { get; }

    public bool HasIcon => Icon is not null;

    /// <summary>
    /// The letters drawn in place of an icon when the program has none.
    ///
    /// Every row gets something. A list where a third of the rows are blank squares reads as
    /// broken, and the blank ones are not a meaningful category -- a program without artwork is
    /// not a program you care less about. Two letters where the name is two words, so "Epic
    /// Games Launcher" is EG rather than E.
    /// </summary>
    public string Monogram
    {
        get
        {
            var words = Name
                .Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0]))
                .ToList();

            return words.Count switch
            {
                0 => "?",
                1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
                _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
            };
        }
    }

    /// <summary>
    /// The monogram's colour, picked from a fixed palette by the program's name.
    ///
    /// Stable across launches because it is derived from the name rather than from a position in
    /// the list, which is what makes it useful: the same program is the same colour every time,
    /// so it can be found again by shape as well as by reading.
    /// </summary>
    public string MonogramBrushKey => MonogramPalette[
        (int)(Hash(Name) % (uint)MonogramPalette.Length)];

    private static readonly string[] MonogramPalette =
        ["Accent", "AccentBright", "RiskSafe", "RiskModerate", "Warning", "Muted"];

    /// <summary>FNV-1a, so the colour does not move when the runtime's string hashing does.</summary>
    public static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
            hash = (hash ^ char.ToUpperInvariant(c)) * 16777619u;

        return hash;
    }

    /// <summary>Publisher and version on one line, with whichever half exists.</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>(2);
            if (App.Publisher is { Length: > 0 } publisher)
                parts.Add(publisher);
            if (App.Version is { Length: > 0 } version)
                parts.Add(version);

            return parts.Count > 0 ? string.Join("  ·  ", parts) : App.InstallLocation ?? "";
        }
    }

    /// <summary>Where it came from, in the reader's language.</summary>
    public string SourceText => Strings.GetOr($"apps.source.{App.Source.ToString().ToLowerInvariant()}",
        App.Source.ToString());

    /// <summary>
    /// Roughly how big it is, or empty when Windows does not know.
    ///
    /// EstimatedSize is absent more often than it is present and wrong a fair share of the time
    /// it is there, so an empty cell is the honest output rather than a confident "0 MB".
    /// </summary>
    public string SizeText => App.SizeBytes <= 0 ? "" : Format(App.SizeBytes);

    public static string Format(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
        > 0 => $"{bytes / 1024.0:0} KB",
        _ => "",
    };

    public string InstalledOnText => App.InstalledOn is { } date ? date.ToString("d") : "";

    /// <summary>
    /// True when this can be removed without anybody answering an installer's questions.
    ///
    /// Said on the row rather than discovered halfway through: "Remove" that silently opens a
    /// wizard on top of the window is a worse surprise than a row that warned you.
    /// </summary>
    public bool IsSilent => App.CanRemoveSilently;

    public bool NeedsElevation => App.IsMachineWide;

    public RemovalRowState State
    {
        get => _state;
        set
        {
            if (SetField(ref _state, value))
            {
                foreach (var property in StateDependent)
                    Raise(property);
            }
        }
    }

    /// <summary>
    /// Everything on the row that changes when its state does.
    ///
    /// A list rather than a run of Raise calls, because the list can be tested and a run of
    /// calls cannot: PropertyChanged is marshalled to a UI thread, and no test has one running,
    /// so an omission here is invisible to every test in the suite.
    ///
    /// <para>One was omitted, and this is what it cost. <see cref="HasStatus"/> was missing while
    /// <see cref="StatusText"/> was present, so a finished removal computed the right sentence,
    /// passed its tests, and never drew it: the row was told the text had changed but not that it
    /// now had one, and the line stayed hidden. What somebody saw after pressing Remove was a row
    /// that dimmed slightly and said nothing, which is what a button that did nothing looks
    /// like.</para>
    /// </summary>
    public static readonly string[] StateDependent =
    [
        nameof(IsWorking),
        nameof(IsInteractive),
        nameof(RowOpacity),
        nameof(StatusText),
        nameof(HasStatus),
    ];

    /// <summary>What the sweep is doing right now, or why it stopped.</summary>
    public string? Detail
    {
        get => _detail;
        set
        {
            if (SetField(ref _detail, value))
                Raise(nameof(StatusText));
        }
    }

    public bool IsWorking => _state is RemovalRowState.Uninstalling or RemovalRowState.Scanning
        or RemovalRowState.Sweeping;

    public bool IsInteractive => _state is RemovalRowState.Idle or RemovalRowState.Failed;

    public double RowOpacity => _state == RemovalRowState.Removed ? 0.45 : 1.0;

    /// <summary>The line under the row while something is happening to it.</summary>
    public string StatusText => _state switch
    {
        RemovalRowState.Uninstalling => Strings.Get("apps.state.uninstalling"),
        RemovalRowState.Scanning => Strings.Get("apps.state.scanning"),
        RemovalRowState.Sweeping => Strings.Format("apps.state.sweeping", _detail ?? ""),
        RemovalRowState.Removed => _detail ?? Strings.Get("apps.state.removed"),
        RemovalRowState.Failed => _detail ?? Strings.Get("apps.state.failed"),
        _ => "",
    };

    public bool HasStatus => _state != RemovalRowState.Idle;

    public string RemoveLabel => Strings.Get("apps.remove");

    public Task RemoveAsync() => IsInteractive ? _remove(this) : Task.CompletedTask;

    /// <summary>
    /// Which row state a removal stage puts the row into.
    ///
    /// Its own method so it can be tested. The transitions used to be an inline switch inside
    /// the progress callback, where the only way to observe them was a PropertyChanged event --
    /// which never fires under test, because the notification is posted to a UI thread that no
    /// test has running.
    /// </summary>
    public static RemovalRowState StateFor(Win32.Apps.RemovalStage stage, RemovalRowState current) => stage switch
    {
        Win32.Apps.RemovalStage.Uninstalling => RemovalRowState.Uninstalling,
        Win32.Apps.RemovalStage.Scanning => RemovalRowState.Scanning,
        Win32.Apps.RemovalStage.Sweeping => RemovalRowState.Sweeping,

        // Finished is not a row state: what the row says next depends on the report, which the
        // window has and this does not. Leaving it alone keeps the spinner up for the moment
        // between the last sweep and the summary.
        _ => current,
    };

    // ------------------------------------------------------------------ details

    private bool _open;
    private bool _loading;
    private AppDetail? _inspection;

    /// <summary>Whether the details panel under this row is open.</summary>
    public bool IsOpen
    {
        get => _open;
        private set
        {
            if (SetField(ref _open, value))
                Raise(nameof(DetailsLabel));
        }
    }

    /// <summary>True while the machine is being read for this row.</summary>
    public bool IsLoadingDetails
    {
        get => _loading;
        private set => SetField(ref _loading, value);
    }

    /// <summary>What was found, or null until the panel has been opened.</summary>
    public AppDetail? Inspection
    {
        get => _inspection;
        private set
        {
            if (SetField(ref _inspection, value))
            {
                Raise(nameof(Evidence));
                Raise(nameof(Leftovers));
                Raise(nameof(HasLeftovers));
                Raise(nameof(RegistrationText));
                Raise(nameof(WhyText));
                Raise(nameof(LeftoverSummary));
                Raise(nameof(CanForget));
                Raise(nameof(ForgetNeedsAdmin));
                Raise(nameof(ShowsUninstallerAdvice));
                Raise(nameof(HasInspection));
            }
        }
    }

    public bool HasInspection => _inspection is not null;


    public string DetailsLabel => Strings.Get(_open ? "apps.details.hide" : "apps.details");

    /// <summary>
    /// Opens the panel, reading the machine the first time.
    ///
    /// Read on demand rather than for every row up front: the leftover scan measures folder
    /// sizes, and doing that a hundred times to fill a list nobody has asked a question about
    /// would make the tab slow to earn nothing.
    /// </summary>
    public async Task ToggleDetailsAsync()
    {
        if (_open)
        {
            IsOpen = false;
            return;
        }

        IsOpen = true;

        if (_inspection is not null)
            return;

        IsLoadingDetails = true;
        try
        {
            // Off the UI thread: this walks folders and adds up their sizes.
            Inspection = await Task.Run(() => AppDetails.For(App)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            IsOpen = false;
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    /// <summary>Forgets what was read, so the panel is rebuilt after the machine changes.</summary>
    public void Invalidate()
    {
        Inspection = null;
        IsOpen = false;
    }

    /// <summary>The exact registry key or package that puts this row in the list.</summary>
    public string RegistrationText => _inspection?.Registration ?? "";

    /// <summary>
    /// The sentence that answers the question, in the strongest form the evidence supports.
    ///
    /// Three cases, and they are genuinely different. Everything present is an ordinary
    /// installed program. Nothing present is a leftover registration, which is worth saying
    /// outright because it is the case somebody came here confused about. In between is in
    /// between, and it says which of the paths are gone rather than guessing what that means --
    /// a game whose files were deleted while its launcher stayed is exactly that shape.
    /// </summary>
    public string WhyText
    {
        get
        {
            if (_inspection is not { } detail)
                return "";

            if (detail.CheckedCount == 0)
                return Strings.Get("apps.details.norecords");

            if (detail.NothingLeft)
                return Strings.Get("apps.details.nothingleft");

            return detail.MissingCount == 0
                ? Strings.Get("apps.details.allthere")
                : Strings.Format("apps.details.somemissing", detail.MissingCount, detail.CheckedCount);
        }
    }

    public IReadOnlyList<EvidenceRow> Evidence =>
        _inspection is null
            ? []
            : [.. _inspection.Evidence.Select(e => new EvidenceRow(
                Strings.Get($"apps.details.item.{e.Item.ToString().ToLowerInvariant()}"),
                e.Path,
                e.Present,
                Strings.Get(e.Present ? "apps.details.present" : "apps.details.missing")))];

    /// <summary>
    /// Everything this entry owns, each with its own button.
    ///
    /// One list rather than a folders list and a keys list: what somebody is deciding is which
    /// of these things to keep, and splitting them by storage medium sorts them by the one
    /// property that does not bear on that decision.
    /// </summary>
    public IReadOnlyList<LeftoverRow> Leftovers =>
        _inspection is null
            ? []
            : [.. _inspection.Removable.Select(i => new LeftoverRow(
                this,
                i.Path,
                Format(i.SizeBytes),
                Strings.Get(i.IsRegistration
                    ? "apps.details.kind.registration"
                    : $"apps.details.kind.{i.Kind.ToString().ToLowerInvariant()}"),
                i.HoldsUserData,
                Strings.Get("apps.details.userdata"),
                i.IsRegistration,
                i.NeedsAdmin))];

    public bool HasLeftovers => _inspection is { Removable.Count: > 0 };

    /// <summary>
    /// True when Forget would be refused: the entry lives under HKLM and this copy is not
    /// elevated.
    ///
    /// Said above the button rather than discovered by it. Forget exports the key before
    /// removing it, so the failure was never dangerous -- it was just silent, and a button that
    /// does nothing is the thing this panel was added to stop happening.
    /// </summary>
    public bool ForgetNeedsAdmin => _inspection is { ForgetNeedsAdmin: true };

    /// <summary>What to do about it, or empty when there is nothing to do about.</summary>
    public string AdminNote => AdminNeeded.Explain;

    /// <summary>
    /// A one-line total, or the sentence that stands in for an empty list.
    ///
    /// Short because the list underneath is the detail: repeating it in prose above would be
    /// the same information twice, and the thing worth knowing at a glance is how much of it
    /// there is.
    /// </summary>
    public string LeftoverSummary
    {
        get
        {
            if (_inspection is not { } detail)
                return "";

            // The entry itself is excluded when deciding whether anything was found -- "only the
            // entry is left" is the useful thing to say -- but counted in the total, because the
            // total sits above a list it is in, and a count that disagrees with the rows under
            // it makes the reader stop and recount rather than read on.
            if (!detail.Removable.Any(i => !i.IsRegistration))
                return Strings.Get("apps.details.nothingondisk");

            var bytes = detail.Removable.Sum(i => i.SizeBytes);

            return bytes > 0
                ? Strings.Format("apps.details.total", detail.Removable.Count, Format(bytes))
                : Strings.Format("apps.details.total.nosize", detail.Removable.Count);
        }
    }

    /// <summary>
    /// Removes one thing this entry owns, leaving the rest alone.
    ///
    /// The panel is rebuilt from the machine afterwards rather than having the row struck out of
    /// the list in memory. It costs a second scan and it means the panel is showing what is
    /// there rather than what this program believes it did -- which is the same reason the sweep
    /// re-enumerates instead of trusting an exit code.
    /// </summary>
    public async Task RemoveItemAsync(string path, Func<string, string, Task<bool>> remove)
    {
        if (!IsInteractive)
            return;

        IsLoadingDetails = true;
        try
        {
            await remove(Id, path).ConfigureAwait(true);

            // A row that has just lost its registration is no longer interactive, and rebuilding
            // its panel would list the entry it no longer has: the registration is built from
            // the id rather than read back, so it cannot notice it has gone. Close it instead --
            // the row's own status now says what happened.
            if (!IsInteractive)
            {
                IsOpen = false;
                return;
            }

            Inspection = await Task.Run(() => AppDetails.For(App)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The panel keeps what it had. A failed removal is reported on the row itself.
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    /// <summary>Whether the registration is a note about a program rather than the program.</summary>
    public bool CanForget => _inspection is { CanForget: true };

    /// <summary>
    /// Whether to point the reader at Remove instead.
    ///
    /// Shown when the program's own files are still where it said they were: its uninstaller
    /// knows things about the installation that a name-matching sweep does not, so running it is
    /// the better route and this button skips it. Not a block -- somebody removing a program
    /// whose uninstaller is broken is a real situation, and they can read this and go ahead.
    /// </summary>
    public bool ShowsUninstallerAdvice =>
        _inspection is { } detail
        && detail.Evidence.Any(e => e.Item == EvidenceItem.InstallFolder && e.Present);

    /// <summary>Re-reads everything the string table feeds.</summary>
    public void RefreshText() => Raise(null);

    /// <summary>
    /// A program's artwork, read but not yet drawable: raw pixels, or a PNG still on disk.
    ///
    /// Exists so the expensive half can happen anywhere. Nothing in here touches Avalonia.
    /// </summary>
    public readonly record struct IconSource(IconPixels? Pixels, string? PngPath);

    /// <summary>
    /// Finds and reads the best icon available for a program. Safe on any thread.
    ///
    /// Which file that is comes from <see cref="AppIcons"/>, which searches the registry and the
    /// disk and has no business being in the window. Reading the pixels out of it is Win32 as
    /// well. Both are what actually costs time, and neither needs the UI thread.
    ///
    /// An empty result is not a failure. The row draws a monogram instead.
    /// </summary>
    public static IconSource ReadIcon(InstalledApp app)
    {
        // Caught broadly, as it is when the pixels are drawn. An icon is decoration; the list is
        // the feature, and one program's missing artwork must not empty the whole collection.
        try
        {
            return AppIcons.For(app) switch
            {
                { Kind: IconKind.Image } image => new IconSource(null, image.Path),
                { Kind: IconKind.FileType } type => new IconSource(FileIcons.TryReadFileType(type.Path), null),
                { Kind: IconKind.Resource } icon => new IconSource(FileIcons.TryReadResource(icon.Path), null),
                _ => default,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return default;
        }
    }

    /// <summary>
    /// Turns what was read into something Avalonia can draw. UI thread only.
    ///
    /// Caught deliberately broadly, and this was found by test: decoding a package's PNG needs
    /// Avalonia's render interface, which does not exist in a headless run, and the
    /// InvalidOperationException it threw escaped the constructor, emptied the whole collection
    /// and left the tab blank.
    /// </summary>
    private static Bitmap? Draw(IconSource icon)
    {
        try
        {
            return icon.PngPath is { } png ? LoadPng(png) : Draw(icon.Pixels);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>A package's logo, which is an ordinary PNG rather than an icon resource.</summary>
    private static Bitmap? LoadPng(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException
                                      or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Wraps the pixels an icon came back as in something Avalonia can draw.</summary>
    private static Bitmap? Draw(IconPixels? icon)
    {
        if (icon is null)
            return null;

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(icon.Width, icon.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (var buffer = bitmap.Lock())
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    icon.Bgra, 0, buffer.Address, icon.Bgra.Length);
            }

            return bitmap;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // An icon is decoration. Nothing about this list stops working without one.
            return null;
        }
    }
}
