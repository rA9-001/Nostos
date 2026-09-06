using System.Collections.ObjectModel;
using Nostos.App.Backends;
using Nostos.App.Localization;
using Nostos.App.Startup;
using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Localization;
using Nostos.Core.Settings;
using Nostos.Core.Updates;
using Nostos.Win32.Updates;
using Nostos.Core.Engine;
using Nostos.Ipc;
using Nostos.Win32.Services;

// The type and the property that lists them want the same name. The alias keeps the
// property called what it should be called on screen.
using Win32Apps = Nostos.Win32.Apps;
using Win32Cleanup = Nostos.Win32.Cleanup;
using Win32Startup = Nostos.Win32.Services.StartupItems;

namespace Nostos.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable, ISettingsHost
{
    private readonly List<TweakItemViewModel> _allTweaks = [];
    private readonly CancellationTokenSource _shutdown = new();
    private ReleaseInfo? _update;

    private readonly IOptimizerBackend? _injectedBackend;

    private IOptimizerBackend? _backend;
    private string _connectionText = Strings.Get("connection.connecting");
    private string? _connectionDetail;
    private bool _isServiceMode;
    private bool _canApplyMachineScope = true;
    private bool _isBusy;
    private string _statusMessage = "";
    private bool _statusIsError;
    private string? _searchText;
    private string _selectedCategory = AllCategories;
    private TweakItemViewModel? _selectedTweak;
    private RunningProcess? _selectedProcess;
    private int _outstandingCount;
    private bool _isSettingUp = true;
    private bool _serviceReady;
    private string? _setupNotice;
    private bool _serviceNeedsRepair;
    private Bootstrapper _bootstrapper = new();
    private readonly ActivityTracker _activity;
    private DateTimeOffset? _lastUpdatedUtc;
    private bool _isLive;
    private Task? _liveLoop;

    /// <summary>Set once the app has been removed from the machine. Nothing may touch it after.</summary>
    private bool _detached;

    /// <summary>
    /// How often the catalog re-reads itself when nothing else is happening.
    ///
    /// The machine is not the only thing that writes these values: Windows Update resets
    /// several of them, the service's reconciler re-applies drifted ones, and the user can
    /// change any of them in Settings while the window is open. A view that is only correct
    /// immediately after you clicked something is a view you cannot trust.
    /// </summary>
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(5);

    // Re-exported so the converters and the tests keep one name for these. The values, and
    // the reasoning behind them, live with the filtering they belong to.
    public const string AllCategories = CatalogFilter.AllCategories;
    public const string NotApplicableCategory = CatalogFilter.NotApplicableCategory;
    public static string NotApplicableHeader => CatalogFilter.NotApplicableHeader;

    /// <param name="backend">
    /// Supplied by tests. When null the view model discovers a backend for itself, preferring
    /// the service and falling back to the in-process engine.
    /// </param>
    /// <param name="activity">
    /// Supplied by tests so the show-delay and minimum-visible thresholds can be driven without
    /// sleeping. Production uses the real clock.
    /// </param>
    /// <param name="remover">
    /// How the app takes itself off the machine. Supplied by tests, because the real one talks
    /// to the Service Control Manager and deletes folders.
    /// </param>
    /// <param name="store">Where preferences are read and written. Supplied by tests.</param>
    public MainWindowViewModel(
        IOptimizerBackend? backend = null,
        ActivityTracker? activity = null,
        ISystemRemover? remover = null,
        ISettingsStore? store = null)
    {
        _injectedBackend = backend;

        _activity = activity ?? new ActivityTracker();
        _activity.PropertyChanged += (_, _) =>
        {
            Raise(nameof(IsWorking));
            Raise(nameof(BusyText));
            Raise(nameof(ActivityTitle));
            Raise(nameof(ActivityDetail));
        };

        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy && !_detached);
        InstallUpdateCommand = new AsyncCommand(
            InstallUpdateAsync, () => _update is not null && !IsBusy && !_detached);
        RevertAllCommand = new AsyncCommand(
            RevertAllAsync, () => !IsBusy && OutstandingCount > 0 && !_detached);
        ApplyCommand = new AsyncCommand(p => ApplyAsync(p as TweakItemViewModel));
        RevertCommand = new AsyncCommand(p => RevertAsync(p as TweakItemViewModel));
        DryRunCommand = new AsyncCommand(p => DryRunAsync(p as TweakItemViewModel));
        ApplyProfileCommand = new AsyncCommand(p => ApplyProfileAsync(p as ProfileViewModel));
        ToggleProfileCommand = new AsyncCommand(p =>
        {
            (p as ProfileViewModel)?.Toggle();
            return Task.CompletedTask;
        });
        RefreshProcessesCommand = new AsyncCommand(() =>
        {
            RefreshProcesses();
            return Task.CompletedTask;
        });
        ToggleTweakCommand = new AsyncCommand(p => p is TweakItemViewModel tweak
            ? tweak.ShowsAsApplied ? RevertAsync(tweak) : ApplyAsync(tweak)
            : Task.CompletedTask);

        RemoveAppCommand = new AsyncCommand(p =>
            p is AppItemViewModel app ? app.RemoveAsync() : Task.CompletedTask);
        ToggleDetailsCommand = new AsyncCommand(p =>
            p is AppItemViewModel app ? app.ToggleDetailsAsync() : Task.CompletedTask);
        ForgetAppCommand = new AsyncCommand(p =>
            p is AppItemViewModel app ? ForgetAppAsync(app) : Task.CompletedTask);
        RemoveItemCommand = new AsyncCommand(p =>
            p is LeftoverRow row ? RemoveItemAsync(row) : Task.CompletedTask);
        ScanCleanupCommand = new AsyncCommand(ReloadCleanupAsync);
        CleanItemCommand = new AsyncCommand(p =>
            p is CleanupItemViewModel item ? CleanAsync(item) : Task.CompletedTask);
        CleanAllCommand = new AsyncCommand(CleanEverythingAsync);
        RefreshAppsCommand = new AsyncCommand(ReloadAppsAsync);

        ToggleStartupCommand = new AsyncCommand(p =>
            p is StartupItemViewModel item ? item.ToggleAsync() : Task.CompletedTask);
        RefreshStartupCommand = new AsyncCommand(ReloadStartupAsync);
        EnableServiceCommand = new AsyncCommand(
            EnableServiceAsync,
            () => !AppPaths.IsPortable && (!_serviceReady || _serviceNeedsRepair) && !IsBusy && !_detached);

        // Built before the commands that close over it, so that neither the compiler nor a
        // reader has to wonder whether a click could arrive first.
        Settings = new SettingsViewModel(this, remover, store);
        Settings.Exit += () => ExitRequested?.Invoke();

        OpenSettingsCommand = new AsyncCommand(() =>
        {
            // The removal plan is built on open rather than on construction: it reads the SCM,
            // and a window that never opens Settings should never pay for that.
            Settings.PrepareRemoval();
            ShowSettings = true;
            return Task.CompletedTask;
        });

        CloseSettingsCommand = new AsyncCommand(() =>
        {
            ShowSettings = false;
            return Task.CompletedTask;
        });

        Strings.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Rewrites the window in the new language, without reloading anything.
    ///
    /// Nothing here goes back to the machine. Every string on screen is either a binding
    /// through the string table, which re-reads itself, or a computed property over data the
    /// window already has, which only needs telling to ask again. Re-running the filter is what
    /// rebuilds the section bands, whose text is the translated group name.
    /// </summary>
    private void OnLanguageChanged()
    {
        // The backend names itself, and that name is on screen, so it has to be re-read too.
        if (_backend is not null)
            ConnectionText = _backend.Description;

        foreach (var tweak in _allTweaks)
            tweak.RefreshText();

        // The journal rows are plain objects with no change notification of their own, so they
        // are rebuilt from the lines they already hold rather than told to re-read. No I/O:
        // the lines are in memory, and only the words wrapped around them change.
        RebuildJournal(Journal.Select(e => e.Line).ToList());

        foreach (var profile in Profiles)
            profile.RefreshText();

        foreach (var item in StartupItems)
            item.RefreshText();

        foreach (var app in _allApps)
            app.RefreshText();

        ApplyFilter();

        if (_lastSummary is not null)
            Report(_lastSummary);

        Raise(nameof(OutstandingText));
        Raise(nameof(ActivityTitle));
        Raise(nameof(ActivityDetail));
        Raise(nameof(LastUpdatedText));
        Raise(nameof(UpdateTitle));
        Raise(nameof(UpdateDetail));
        Raise(nameof(SetupBannerTitle));
        Raise(nameof(SetupActionLabel));
        Raise(nameof(SelectedCategoryPromise));
        Raise(nameof(TargetNote));
        Raise(nameof(StartupSummary));
        Raise(nameof(UpdateSummary));
        Raise(nameof(AppsSummary));
        Raise(nameof(AppsBlurb));
        Raise(nameof(AppsBackupPath));
    }

    /// <summary>
    /// Raised when the app should close itself, which happens exactly once: after the user has
    /// removed it and clicked the button that says so.
    /// </summary>
    public event Action? ExitRequested;

    // ------------------------------------------------------------- collections

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategories];
    public ObservableCollection<JournalEntryViewModel> Journal { get; } = [];
    public ObservableCollection<ProfileViewModel> Profiles { get; } = [];

    /// <summary>Live checklist shown while the app sets itself up on first launch.</summary>
    public ObservableCollection<StartupStep> SetupSteps => _bootstrapper.Steps;

    // ---------------------------------------------------------------- commands

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand InstallUpdateCommand { get; }
    public AsyncCommand RevertAllCommand { get; }
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand RevertCommand { get; }
    public AsyncCommand DryRunCommand { get; }
    public AsyncCommand ApplyProfileCommand { get; }

    /// <summary>
    /// Opens or closes a profile card.
    ///
    /// A command rather than a click handler in the view, because the whole header strip is the
    /// hit target: a nine-pixel arrow is a fiddly thing to ask somebody to hit, and the card is
    /// the thing they are looking at.
    /// </summary>
    public AsyncCommand ToggleProfileCommand { get; }
    public AsyncCommand EnableServiceCommand { get; }
    public AsyncCommand OpenSettingsCommand { get; }
    public AsyncCommand CloseSettingsCommand { get; }

    /// <summary>Updates, and removing the app. See <see cref="SettingsViewModel"/>.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>True while the settings panel is covering the window.</summary>
    public bool ShowSettings
    {
        get;
        private set => SetField(ref field, value);
    }

    // -------------------------------------------------------------- properties

    public string ConnectionText
    {
        get => _connectionText;
        private set => SetField(ref _connectionText, value);
    }

    /// <summary>Why the app fell back to the in-process engine, if it did.</summary>
    public string? ConnectionDetail
    {
        get => _connectionDetail;
        private set
        {
            if (SetField(ref _connectionDetail, value))
                Raise(nameof(HasConnectionDetail));
        }
    }

    public bool HasConnectionDetail => !string.IsNullOrEmpty(_connectionDetail);

    public bool IsServiceMode
    {
        get => _isServiceMode;
        private set => SetField(ref _isServiceMode, value);
    }

    public bool CanApplyMachineScope
    {
        get => _canApplyMachineScope;
        private set
        {
            if (SetField(ref _canApplyMachineScope, value))
            {
                Raise(nameof(ShowElevationWarning));
                Raise(nameof(ShowSetupBanner));

                // Pushed down to the rows rather than left for each to ask, because a row is
                // built from what came over the pipe and has no way to know where that pipe
                // goes. Without this the banner said machine-wide changes were unavailable
                // while thirty rows underneath it went on offering to make them.
                foreach (var tweak in _allTweaks)
                    tweak.MachineScopeAvailable = value;

                foreach (var entry in StartupItems)
                    entry.MachineScopeAvailable = value;
            }
        }
    }

    public bool ShowElevationWarning => !_canApplyMachineScope || !_serviceReady;

    /// <summary>
    /// The banner also carries notices that have nothing to do with elevation -- an orphaned
    /// service registration, an enforced Smart App Control -- so it is shown for those too.
    /// </summary>
    public bool ShowSetupBanner => ShowElevationWarning || HasSetupNotice;

    public string SetupBannerTitle =>
        !_serviceReady ? Strings.Get("banner.service.missing")
        : _serviceNeedsRepair ? Strings.Get("banner.service.broken")
        : Strings.Get("banner.service.known");

    /// <summary>
    /// Only the missing-service case needs the standing explanation of what is lost, and only
    /// when installing the service is actually an option. In portable mode it is not, and the
    /// portable notice already says what the trade-off is.
    /// </summary>
    public bool ShowServiceExplanation => !_serviceReady && !IsPortableMode;

    /// <summary>True when this copy keeps its data beside the executable and installs nothing.</summary>
    public bool IsPortableMode => AppPaths.IsPortable;

    /// <summary>
    /// Whether to offer the setup button at all. Portable mode has nothing to enable: there is
    /// no service executable to install, and installing one would contradict the mode.
    /// </summary>
    public bool CanOfferServiceSetup => !IsPortableMode;

    /// <summary>Repairing an existing registration is a different act from enabling a new one.</summary>
    public string SetupActionLabel =>
        _serviceReady ? Strings.Get("banner.service.repair") : Strings.Get("banner.service.enable");

    /// <summary>True when the service works now but will not survive a restart.</summary>
    public bool ServiceNeedsRepair
    {
        get => _serviceNeedsRepair;
        private set
        {
            if (!SetField(ref _serviceNeedsRepair, value))
                return;

            Raise(nameof(SetupBannerTitle));
            EnableServiceCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>True while the startup checklist is running, which the window shows as an overlay.</summary>
    public bool IsSettingUp
    {
        get => _isSettingUp;
        private set => SetField(ref _isSettingUp, value);
    }

    /// <summary>True when the privileged service is installed, running and reachable.</summary>
    public bool ServiceReady
    {
        get => _serviceReady;
        private set
        {
            if (!SetField(ref _serviceReady, value))
                return;

            Raise(nameof(ShowElevationWarning));
            Raise(nameof(ShowSetupBanner));
            Raise(nameof(SetupBannerTitle));
            Raise(nameof(ShowServiceExplanation));
            Raise(nameof(SetupActionLabel));
            EnableServiceCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>What the setup could not do, phrased for a human. Null when everything worked.</summary>
    public string? SetupNotice
    {
        get => _setupNotice;
        private set
        {
            if (SetField(ref _setupNotice, value))
            {
                Raise(nameof(HasSetupNotice));
                Raise(nameof(ShowSetupBanner));
            }
        }
    }

    public bool HasSetupNotice => !string.IsNullOrEmpty(_setupNotice);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;

            Raise(nameof(IsWorking));
            RefreshCommand.RaiseCanExecuteChanged();
            RevertAllCommand.RaiseCanExecuteChanged();
            EnableServiceCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// What is happening right now, or null when nothing is.
    ///
    /// Separate from <see cref="StatusMessage"/>, which is the result of the *last* thing.
    /// Collapsing the two meant the outcome of an apply was overwritten by "workingâ€¦" the next
    /// time anything ran, and there was nowhere to say what the spinner was spinning for.
    /// </summary>
    public string? BusyText => _activity.Caption;

    /// <summary>
    /// Whether the activity panel should be showing progress rather than the last result.
    ///
    /// Deliberately not "is an operation running". Work that finishes in a few milliseconds
    /// never sets this, because a spinner that appears and vanishes inside two frames reads as
    /// a glitch. <see cref="ActivityTracker"/> owns that decision.
    /// </summary>
    public bool IsWorking => _activity.IsVisible;

    /// <summary>The activity panel's headline: what is happening, or what last happened.</summary>
    public string ActivityTitle => _activity.IsVisible
        ? _activity.Caption ?? Strings.Get("activity.working")
        : string.IsNullOrWhiteSpace(StatusMessage) ? Strings.Get("activity.ready") : StatusMessage;

    /// <summary>
    /// The activity panel's second line: what the headline means, or what to do next.
    ///
    /// Falls back to the standing summary of the machine when the last operation had nothing
    /// more to add, so the line is never empty and never stale.
    /// </summary>
    public string ActivityDetail
    {
        get
        {
            if (_activity.IsVisible)
                return Strings.Get("activity.saved.first");

            if (!string.IsNullOrWhiteSpace(StatusDetail))
                return StatusDetail;

            return OutstandingCount == 0
                ? Strings.Get("activity.changes.none")
                : OutstandingCount == 1
                    ? Strings.Get("activity.changes.one")
                    : Strings.Format("activity.changes.many", OutstandingCount);
        }
    }

    /// <summary>Explanation for the last result, or null when there is nothing to add.</summary>
    public string? StatusDetail
    {
        get;
        private set => SetField(ref field, value);
    }

    /// <summary>True once the background refresh loop is running.</summary>
    public bool IsLive
    {
        get => _isLive;
        private set => SetField(ref _isLive, value);
    }

    /// <summary>
    /// How stale the displayed state is, in words. Shown next to the live indicator so that
    /// "nothing is outdated" is something the user can check rather than something we assert.
    /// </summary>
    public string LastUpdatedText
    {
        get
        {
            if (_lastUpdatedUtc is not { } at)
                return Strings.Get("activity.updated.never");

            var age = DateTimeOffset.UtcNow - at;
            return age.TotalSeconds switch
            {
                < 3 => Strings.Get("activity.updated.live"),
                < 60 => Strings.Format("activity.updated.seconds", (int)age.TotalSeconds),
                < 3600 => Strings.Format("activity.updated.minutes", (int)age.TotalMinutes),
                _ => Strings.Get("activity.updated.stale"),
            };
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetField(ref _statusIsError, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                ApplyFilter();
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        // Coalesce null: the bound ListBox reports a null selection whenever its items are
        // rebuilt, and a null category would filter every row out of the catalog.
        set
        {
            var effective = value ?? AllCategories;

            if (SetField(ref _selectedCategory, effective))
            {
                Raise(nameof(SelectedCategoryPromise));
                ApplyFilter();
            }
            else if (value is null)
            {
                // The value did not change, so SetField raised nothing -- but the control that
                // sent the null still shows no selection. Push the coalesced value back to it.
                Raise();
            }
        }
    }

    /// <summary>
    /// What the selected category claims about the tweaks inside it, or null for "all".
    ///
    /// Shown under the filter list so the heading is not left to carry the meaning on its own:
    /// "FPS" is a promise, and the user is entitled to see it spelled out before deciding that
    /// six rows under it are worth applying.
    /// </summary>
    public string? SelectedCategoryPromise => CatalogFilter.PromiseOf(_selectedCategory);

    public TweakItemViewModel? SelectedTweak
    {
        get => _selectedTweak;
        set
        {
            if (!SetField(ref _selectedTweak, value))
                return;

            Raise(nameof(HasSelection));
            Raise(nameof(NeedsTargetProcess));
            Raise(nameof(TargetNote));

            // Filled the moment a process-scoped row is selected, so the picker is populated by
            // the time the reader looks at it. Refreshed then rather than once at startup
            // because the interesting process is usually one they started after opening this.
            if (NeedsTargetProcess)
                RefreshProcesses();
        }
    }

    public bool HasSelection => _selectedTweak is not null;

    // ------------------------------------------------- the target of a process-scoped tweak

    /// <summary>
    /// True when the selected tweak acts on one running process and therefore needs to be told
    /// which.
    ///
    /// Two tweaks in the catalog are like this. process.game-tuning was unusable from the window
    /// for as long as the window had no way to say: it reported itself not applicable with "no
    /// target process specified", which is true and useless, and the only way to use it at all
    /// was `nos apply process.game-tuning --pid`. process.persistent-priority needs the same
    /// answer for a different reason -- it wants the executable's name, not the process -- and
    /// picking a running copy of the game is the least error-prone way to spell it.
    /// </summary>
    public bool NeedsTargetProcess => _selectedTweak?.NeedsTarget == true;

    public ObservableCollection<RunningProcess> Processes { get; } = [];

    /// <summary>
    /// What the picker says the chosen program is for, which is not the same sentence for
    /// both tweaks that show one.
    ///
    /// A process-scoped tweak acts on that one process and dies with it. A machine-scoped
    /// one is only using the process to spell an executable's name, and what it writes
    /// outlives it -- telling somebody it "is undone when that process exits" would be the
    /// opposite of true.
    /// </summary>
    public string TargetNote => Strings.Get(_selectedTweak?.Scope == TweakScope.Process
        ? "tweaks.target.note"
        : "tweaks.target.note.persistent");

    /// <summary>
    /// The process the next apply will act on, or null while nothing is chosen.
    ///
    /// Setting it re-reads the tweak against that process, which is what moves the row out of
    /// "not applicable": applicability for this one is not a fact about the machine, it is a
    /// fact about whether the question has been answered yet.
    /// </summary>
    public RunningProcess? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (!SetField(ref _selectedProcess, value))
                return;

            ApplyCommand.RaiseCanExecuteChanged();
            DryRunCommand.RaiseCanExecuteChanged();

            if (_selectedTweak is { } tweak && value is not null)
                _ = RefreshTweakAsync(tweak);
        }
    }

    public AsyncCommand RefreshProcessesCommand { get; }

    // ------------------------------------------------------------------- startup

    /// <summary>
    /// Everything that runs when you sign in.
    ///
    /// Read in-process rather than over the pipe: it needs no privilege, so a round trip to be
    /// told what a plain registry read would have said is latency for nothing -- and it means
    /// the tab works before the service is installed. Only switching an entry goes through the
    /// backend, and only machine-wide ones actually leave this process.
    /// </summary>
    public ObservableCollection<StartupItemViewModel> StartupItems { get; } = [];

    /// <summary>
    /// Everything this program can do about Windows Update, on one page.
    ///
    /// Drawn from the "windows-update" tag rather than a category, because these tweaks are
    /// deliberately spread across three of them: stopping a driver swap is Crashes and Freezes,
    /// a restart toast is Interruptions, and a background download competing with the game for
    /// the link is Ping. A category is a claim about what a tweak does for the player, and
    /// "which part of Windows it writes to" is not one -- so the categories stay as they are
    /// and this tab is a second way in, for a reader who arrived thinking "Windows Update"
    /// rather than thinking "ping".
    ///
    /// The same view models as the Tweaks tab, not copies: a row switched here is the same
    /// object that is already on screen there, so the two can never disagree.
    /// </summary>
    public ObservableCollection<TweakItemViewModel> UpdateTweaks { get; } = [];

    /// <summary>The tag that puts a tweak on the Windows Update tab.</summary>
    public const string UpdateTag = "windows-update";

    /// <summary>How many of them are on, for the chip above the list.</summary>
    public string UpdateSummary => Strings.Format(
        "updates.summary", UpdateTweaks.Count(t => t.ShowsAsApplied), UpdateTweaks.Count);

    /// <summary>
    /// Switch one tweak the way the Startup tab switches a program: on if it is off, off if it
    /// is on. The Tweaks tab keeps Apply, Dry run and Revert as separate buttons, because that
    /// is a page for reading a tweak before deciding. This is a page for deciding.
    /// </summary>
    public AsyncCommand ToggleTweakCommand { get; }

    public AsyncCommand ToggleStartupCommand { get; }

    public AsyncCommand RefreshStartupCommand { get; }

    // ------------------------------------------------------------------ installed programs

    /// <summary>
    /// Everything installed on this machine, from every source that can be enumerated.
    ///
    /// Read in-process rather than over the pipe, the same as the startup list and for the same
    /// reason: enumerating needs no privilege, so a round trip to be told what a registry read
    /// would have said is latency for nothing, and the tab works before the service exists.
    /// Only the removal goes through a backend.
    /// </summary>
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    private readonly List<AppItemViewModel> _allApps = [];

    public AsyncCommand RemoveAppCommand { get; }

    /// <summary>Opens or closes the panel explaining why a row is in the list.</summary>
    public AsyncCommand ToggleDetailsCommand { get; }

    /// <summary>Removes a registration that outlived its program, and what it left behind.</summary>
    public AsyncCommand ForgetAppCommand { get; }

    /// <summary>Removes one thing an entry owns: a folder, a key, or the entry itself.</summary>
    public AsyncCommand RemoveItemCommand { get; }

    /// <summary>Measures the places that can be emptied.</summary>
    public AsyncCommand ScanCleanupCommand { get; }

    /// <summary>Empties one of them.</summary>
    public AsyncCommand CleanItemCommand { get; }

    /// <summary>Empties every one that has anything in it.</summary>
    public AsyncCommand CleanAllCommand { get; }

    public AsyncCommand RefreshAppsCommand { get; }

    private string _appSearch = "";

    /// <summary>
    /// Filters the list as you type.
    ///
    /// A hundred-odd rows is more than anybody scrolls, and the question people arrive with is
    /// almost always about one program they can already name.
    /// </summary>
    public string AppSearch
    {
        get => _appSearch;
        set
        {
            if (SetField(ref _appSearch, value ?? ""))
                ApplyAppFilter();
        }
    }

    public string AppsSummary => Strings.Format("apps.summary", Apps.Count, _allApps.Count);

    /// <summary>The line under the heading. Carries the total, so the count is stated once.</summary>
    public string AppsBlurb => Strings.Format("apps.blurb", _allApps.Count);

    /// <summary>
    /// Where the registry backups a sweep writes are kept.
    ///
    /// On the page rather than only in the documentation. "Registry keys are exported before they
    /// are removed" is a promise, and a promise about a file is worth much less if the reader
    /// cannot see where the file went.
    /// </summary>
    public string AppsBackupPath => Strings.Format("apps.backups", Win32Apps.AppRemoval.BackupDirectory);

    /// <summary>
    /// Re-reads what is installed, including the icons, off the UI thread.
    ///
    /// The registry walk always went to the pool. The rows did not, and the rows are where the
    /// cost is: each one extracts the program's icon, and a hundred of those measured 81ms of a
    /// window that could not repaint. They are built on the pool now and handed over here.
    ///
    /// <para>What must stay on the UI thread is the handover itself. Filling the bound
    /// collection from a worker crashed the window with "Collection was modified; enumeration
    /// operation may not execute", thrown from inside Avalonia's own item generator as it
    /// walked the list being rebuilt underneath it. So the pool builds a plain list and this
    /// method, back on the UI thread, is the only thing that touches what is bound.</para>
    /// </summary>
    private async Task ReloadAppsAsync()
    {
        var read = await Task.Run(
            () => Win32Apps.InstalledApps.List()
                .Select(app => (App: app, Icon: AppItemViewModel.ReadIcon(app)))
                .ToList(),
            _shutdown.Token).ConfigureAwait(true);

        _allApps.Clear();
        _allApps.AddRange(read.Select(r => new AppItemViewModel(r.App, RemoveAppAsync, r.Icon)));

        ApplyAppFilter();
    }

    private void ApplyAppFilter()
    {
        Apps.Clear();

        foreach (var app in _allApps.Where(a => Matches(a, _appSearch)))
            Apps.Add(app);

        Raise(nameof(AppsSummary));
        Raise(nameof(AppsBlurb));
    }

    private static bool Matches(AppItemViewModel app, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return app.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || (app.App.Publisher?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    /// <summary>
    /// Uninstalls one program, showing each stage on its row as it happens.
    ///
    /// The row is not removed from the list when it finishes. What the reader wants at that
    /// moment is the sentence saying what was swept up, and a row that vanished the instant the
    /// work completed would take the answer with it. The next refresh drops it.
    /// </summary>
    // ------------------------------------------------------------------ reclaiming space

    /// <summary>Every place that can be emptied, measured.</summary>
    public ObservableCollection<CleanupItemViewModel> Cleanup { get; } = [];

    private bool _scanningCleanup;
    private bool _cleanupWanted;

    /// <summary>
    /// Called when the Cleanup tab is first shown.
    ///
    /// Not simply the command, because of the order these can happen in. The tab can be on
    /// screen before <see cref="InitialiseAsync"/> has a backend -- at startup, or on a machine
    /// slow enough that the window is up before the service answers -- and a scan that ran then
    /// would find no backend, return quietly, and leave the tab reading "nothing to reclaim"
    /// over a machine with nine gigabytes in it. So the request is remembered and honoured when
    /// there is something to honour it with.
    /// </summary>
    public Task RequestCleanupAsync()
    {
        _cleanupWanted = true;
        return _backend is null ? Task.CompletedTask : ReloadCleanupAsync();
    }

    /// <summary>True while the machine is being measured.</summary>
    public bool IsScanningCleanup
    {
        get => _scanningCleanup;
        private set
        {
            if (SetField(ref _scanningCleanup, value))
            {
                Raise(nameof(CanClean));
                Raise(nameof(CanCleanAnything));
            }
        }
    }

    /// <summary>How much is there, across everything that can actually be emptied.</summary>
    public string CleanupSummary
    {
        get
        {
            var bytes = Cleanup.Where(c => c.CanClean).Sum(c => c.Bytes);

            return bytes > 0
                ? Strings.Format("clean.summary", AppItemViewModel.Format(bytes))
                : Strings.Get("clean.nothing");
        }
    }

    /// <summary>True when there is something the button could actually empty.</summary>
    public bool CanClean => !_scanningCleanup && Cleanup.Any(c => c.CanClean);

    /// <summary>
    /// Whether "Clean everything" is offered at all, which is the looser question.
    ///
    /// A page whose only remaining rows are the machine-wide ones has something to say, and a
    /// greyed-out button says none of it. Pressing it there marks those rows and states what
    /// would reach them, which is the answer somebody standing in front of that page wants.
    /// </summary>
    public bool CanCleanAnything => !_scanningCleanup
        && Cleanup.Any(c => c.CanClean || (c.NeedsAdmin && c.HasAnything));

    /// <summary>
    /// How much an elevated launch would additionally reach.
    ///
    /// Worth a line of its own above the list. The four machine-wide places are measured either
    /// way -- that was always deliberate -- but until this line existed the only place that
    /// measurement appeared was as four rows the headline total quietly disagreed with.
    /// </summary>
    public string CleanupAdminNote
    {
        get
        {
            var bytes = Cleanup.Where(c => c.NeedsAdmin).Sum(c => c.Bytes);

            return bytes > 0 ? AdminNeeded.More(AppItemViewModel.Format(bytes)) : "";
        }
    }

    public bool HasCleanupAdminNote => CleanupAdminNote.Length > 0;

    /// <summary>What this refuses to look at, and why, straight from the catalogue.</summary>
    public string CleanupNever => Strings.Format(
        "clean.never",
        string.Join(", ", Win32Cleanup.CleanupTargets.NeverTouched.Select(n => n.Path)));

    /// <summary>
    /// Measures everything.
    ///
    /// Done on demand rather than at startup: it walks several directory trees and adds up file
    /// sizes, and a tab nobody has opened has not earned that. The rows are built here and only
    /// mutated on the UI thread -- filling a bound collection from the thread pool is what
    /// crashed the Apps tab with "collection was modified" out of Avalonia's own item generator.
    /// </summary>
    private async Task ReloadCleanupAsync()
    {
        if (_backend is null)
            return;

        IsScanningCleanup = true;
        try
        {
            var findings = await _backend.ScanCleanupAsync(_shutdown.Token).ConfigureAwait(true);
            var existing = Cleanup.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            Cleanup.Clear();

            foreach (var finding in findings)
            {
                if (existing.TryGetValue(finding.Target.Id, out var row))
                {
                    row.Update(finding);
                    Cleanup.Add(row);
                }
                else
                {
                    Cleanup.Add(new CleanupItemViewModel(finding));
                }
            }

            Raise(nameof(CleanupSummary));
            Raise(nameof(CanClean));
            Raise(nameof(CanCleanAnything));
            Raise(nameof(CleanupAdminNote));
            Raise(nameof(HasCleanupAdminNote));
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
        {
            // A measurement that fails leaves the previous numbers, which are stale rather than
            // wrong. Nothing here has changed the machine.
        }
        finally
        {
            IsScanningCleanup = false;
        }
    }

    /// <summary>Empties one place and says what it got.</summary>
    private async Task CleanAsync(CleanupItemViewModel item)
    {
        if (_backend is null)
            return;

        // Answered here rather than in the sweep. The backend would refuse this too, and would
        // be right to, but a refusal that arrives as a result reads as something having gone
        // wrong -- and nothing has: the machine is fine, this copy simply cannot reach that
        // folder. Saying so before the attempt is the difference between an error and an
        // instruction.
        if (item.NeedsAdmin)
        {
            item.State = CleanupRowState.NeedsAdmin;
            item.Detail = null;
            SetStatus(AdminNeeded.Status, isError: true);
            return;
        }

        if (!item.CanClean)
            return;

        item.State = CleanupRowState.Working;

        var activity = _activity.Begin(Strings.Format("clean.activity", item.Name));
        try
        {
            var result = await _backend
                .CleanAsync(item.Id, null, _shutdown.Token)
                .ConfigureAwait(true);

            item.Describe(result);

            // Re-measured rather than assumed: the row now shows what is there, not what this
            // program believes it removed.
            item.Update(await Task.Run(
                () => Win32Cleanup.Cleanup.Measure(item.Target, Win32Cleanup.Cleanup.IsElevated),
                _shutdown.Token).ConfigureAwait(true));

            Raise(nameof(CleanupSummary));
            Raise(nameof(CanClean));
            Raise(nameof(CanCleanAnything));
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
        {
            item.State = CleanupRowState.Failed;
            item.Detail = e.Message;
        }
        finally
        {
            await activity.DisposeAsync();
        }
    }

    /// <summary>
    /// Empties everything that has something in it, one at a time.
    ///
    /// Sequentially on purpose. These are disk-bound and running them together would make the
    /// whole thing slower while making the progress meaningless, and a row at a time is what the
    /// window can honestly show.
    /// </summary>
    private async Task CleanEverythingAsync()
    {
        foreach (var item in Cleanup.Where(c => c.CanClean).ToList())
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            await CleanAsync(item).ConfigureAwait(true);
        }

        // The rows this could not take are marked rather than passed over in silence. "Clean
        // everything listed" that leaves four rows sitting there at their full size has to
        // account for them, or it has broken its own promise in front of the reader.
        foreach (var item in Cleanup.Where(c => c.NeedsAdmin && c.HasAnything))
            item.State = CleanupRowState.NeedsAdmin;

        if (Cleanup.Any(c => c.NeedsAdmin && c.HasAnything))
            SetStatus(AdminNeeded.Status, isError: true);
    }

    /// <summary>
    /// Removes one thing an entry owns.
    ///
    /// The row and the path travel together because that pairing is what the removal is checked
    /// against: the backend rebuilds the list of what the program owns from the live machine and
    /// refuses a path that is not on it. A command that took a path alone would be a way to
    /// recycle anything on the machine.
    /// </summary>
    private async Task RemoveItemAsync(LeftoverRow row)
    {
        if (_backend is null)
            return;

        var app = row.Owner;

        // The panel read this off the machine when it was opened, so the answer is already on
        // the row. Attempting it anyway would export nothing, delete nothing, and come back
        // with reg.exe's own wording, which says access is denied without saying whose.
        if (row.NeedsAdmin)
        {
            app.State = RemovalRowState.Failed;
            app.Detail = AdminNeeded.Explain;
            return;
        }

        await app.RemoveItemAsync(row.Path, async (id, path) =>
        {
            var report = await _backend.RemoveItemAsync(id, path, null, _shutdown.Token).ConfigureAwait(true);

            // Both outcomes are stated on the row, and that is the whole point of this block.
            // The first version reported neither for the entry itself: it reloaded the tab, and
            // the tab's reload does not rebuild the installed list. So a successful removal and
            // a failed one produced exactly the same thing on screen -- nothing -- and the only
            // way to find out which had happened was to open regedit.
            if (report.Problems.Count > 0)
            {
                app.State = RemovalRowState.Failed;
                app.Detail = report.Problems[0];
                return false;
            }

            if (row.IsRegistration)
            {
                // Removing the registration takes the program out of Windows' own list, so the
                // row now describes something that is not there. Marked the way a finished
                // removal is marked rather than made to vanish: the sentence saying what
                // happened is what somebody pressed the button for, and a row that tidies itself
                // away takes the answer with it.
                app.State = RemovalRowState.Removed;
                app.Detail = Strings.Get("apps.details.entrygone");

                // For the History tab, which is where the line about it lands.
                await ReloadAsync().ConfigureAwait(true);
            }

            return true;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Removes an entry that outlived its program, together with whatever was found under its
    /// name.
    ///
    /// Deliberately a separate command from Remove rather than a mode of it. The two make
    /// different claims: Remove says "uninstall this", and this says "the program is gone, take
    /// its entry away as well". A single button that quietly did whichever seemed to apply would
    /// be guessing on the reader's behalf about the one thing they are here to decide.
    /// </summary>
    private async Task ForgetAppAsync(AppItemViewModel app)
    {
        if (_backend is null)
            return;

        if (app.ForgetNeedsAdmin)
        {
            app.State = RemovalRowState.Failed;
            app.Detail = AdminNeeded.Explain;
            return;
        }

        app.Detail = null;
        app.State = RemovalRowState.Scanning;

        var activity = _activity.Begin(Strings.Format("apps.activity", app.Name));
        try
        {
            var report = await _backend.ForgetAppAsync(
                app.Id,
                progress =>
                {
                    app.State = AppItemViewModel.StateFor(progress.Stage, app.State);

                    if (progress.Stage == Win32Apps.RemovalStage.Sweeping)
                        app.Detail = progress.Detail;

                    return Task.CompletedTask;
                },
                _shutdown.Token).ConfigureAwait(true);

            Describe(app, report);
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
        {
            app.State = RemovalRowState.Failed;
            app.Detail = e.Message;
        }
        finally
        {
            await activity.DisposeAsync();
            await ReloadAsync();
        }
    }

    private async Task RemoveAppAsync(AppItemViewModel app)
    {
        if (_backend is null)
            return;

        app.Detail = null;
        app.State = RemovalRowState.Uninstalling;

        var activity = _activity.Begin(Strings.Format("apps.activity", app.Name));
        try
        {
            var report = await _backend.RemoveAppAsync(
                app.Id,
                progress =>
                {
                    app.State = AppItemViewModel.StateFor(progress.Stage, app.State);

                    if (progress.Stage == Win32Apps.RemovalStage.Sweeping)
                        app.Detail = progress.Detail;

                    return Task.CompletedTask;
                },
                _shutdown.Token).ConfigureAwait(true);

            Describe(app, report);
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
        {
            app.State = RemovalRowState.Failed;
            app.Detail = e.Message;
        }
        finally
        {
            await activity.DisposeAsync();
            await ReloadAsync();
        }
    }

    /// <summary>
    /// Turns a removal report into the sentence on the row.
    ///
    /// A removal that could not finish says so on the row rather than only in the status bar,
    /// because by the time somebody reads the status bar they have usually moved on to the next
    /// program in the list.
    /// </summary>
    private void Describe(AppItemViewModel app, Win32Apps.RemovalReport report)
    {
        if (report.Problems.Count > 0)
        {
            app.State = report.FoldersRecycled + report.KeysRemoved > 0
                ? RemovalRowState.Removed
                : RemovalRowState.Failed;

            app.Detail = report.Problems[0];
            SetStatus(Strings.Format("apps.failed", app.Name, report.Problems[0]), isError: true);
            return;
        }

        app.State = RemovalRowState.Removed;
        app.Detail = report.FoldersRecycled + report.KeysRemoved == 0
            ? Strings.Get("apps.removed.clean")
            : Strings.Format(
                "apps.removed.swept",
                report.FoldersRecycled,
                report.KeysRemoved,
                AppItemViewModel.Format(report.BytesReclaimed));

        SetStatus(Strings.Format("apps.done", app.Name));
    }

    private string? _startupError;

    /// <summary>Why the last switch did not take, or null when nothing has gone wrong.</summary>
    public string? StartupError
    {
        get => _startupError;
        private set
        {
            if (SetField(ref _startupError, value))
                Raise(nameof(HasStartupError));
        }
    }

    public bool HasStartupError => _startupError is not null;

    /// <summary>How many of the listed entries actually run, for the line above the list.</summary>
    public string StartupSummary => Strings.Format(
        "startup.summary", StartupItems.Count(i => i.IsEnabled), StartupItems.Count);

    /// <summary>
    /// Rebuilds the startup list, doing the reading and the icons off the UI thread.
    ///
    /// It used to be synchronous on the grounds that five registry keys are not worth an await.
    /// The registry was never the cost: each row also pulls the program's icon out of its
    /// executable, which is a shell call apiece, and fifteen of those measured 92ms of a window
    /// that could not repaint. What comes back here is a list of rows to hand to the bound
    /// collection, which is the one part that has to be on this thread.
    /// </summary>
    private async Task ReloadStartupAsync()
    {
        var machineScope = _canApplyMachineScope;

        var read = await Task.Run(
            () => Win32Startup.List().ToWire()
                .Select(entry => (Entry: entry, Icon: StartupItemViewModel.ReadIcon(entry.ExecutablePath)))
                .ToList(),
            _shutdown.Token).ConfigureAwait(true);

        StartupError = null;
        StartupItems.Clear();

        foreach (var (entry, icon) in read)
        {
            StartupItems.Add(new StartupItemViewModel(entry, ToggleStartupAsync, icon)
            {
                MachineScopeAvailable = machineScope,
            });
        }

        Raise(nameof(StartupSummary));
    }

    /// <summary>
    /// Switches one entry, then re-reads it rather than assuming.
    ///
    /// The row shows what the machine says, not what was asked for. A machine-wide entry goes to
    /// the service and can be refused -- an unelevated app with no service installed cannot
    /// write HKLM -- and a row that flipped anyway would be lying about the state of the machine,
    /// which is the one thing this program must not do.
    /// </summary>
    private async Task ToggleStartupAsync(StartupItemViewModel item, bool enabled)
    {
        StartupError = null;

        if (_backend is null)
        {
            StartupError = Strings.Get("startup.error.nobackend");
            return;
        }

        // Before the write rather than after the refusal. The re-read below is what makes a
        // refused write honest, and it works -- but the switch flicking and springing back is
        // the visual language of a broken control, and the sentence that used to accompany it
        // came from whatever threw inside the registry API.
        if (item.NeedsAdmin)
        {
            StartupError = item.AdminNote;
            return;
        }

        try
        {
            var result = await _backend
                .SetStartupEnabledAsync(item.Id, enabled, _shutdown.Token)
                .ConfigureAwait(true);

            if (!result.Ok)
                StartupError = result.Message;
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
        {
            StartupError = e.Message;
        }

        // Re-read whether or not the write reported success: a refusal and a success that did
        // not stick look identical from here, and both have to leave the row telling the truth.
        var live = Win32Startup.List()
            .FirstOrDefault(i => string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase));

        if (live is not null)
            item.Update(live.ToWire());
        else
            StartupItems.Remove(item);

        Raise(nameof(StartupSummary));

        // The History tab is the record of what this program did to the machine, and a switch
        // flicked here is one of those things. Re-read rather than appending a row locally, so
        // the tab shows what is actually on disk -- including a line the service wrote for a
        // machine-wide entry, which this process never saw.
        await ReloadJournalAsync().ConfigureAwait(true);
    }

    /// <summary>The target to send with a change, or null for every tweak that does not take one.</summary>
    private TweakTarget? TargetFor(TweakItemViewModel tweak)
        => tweak.NeedsTarget && _selectedProcess is { } process
            ? new TweakTarget(process.Id, process.Name)
            : null;

    private void RefreshProcesses()
    {
        var previous = _selectedProcess?.Id;

        Processes.Clear();
        foreach (var process in RunningProcesses.List())
            Processes.Add(process);

        // Keep the choice across a refresh when that process is still running, so pressing
        // Refresh to pick up a game that has just started does not clear the one already
        // chosen. Cleared when it has exited, because a stale pid is the one thing here that
        // could act on the wrong process.
        SelectedProcess = previous is { } id
            ? Processes.FirstOrDefault(p => p.Id == id)
            : null;
    }

    public int OutstandingCount
    {
        get => _outstandingCount;
        private set
        {
            if (!SetField(ref _outstandingCount, value))
                return;

            Raise(nameof(OutstandingText));
            Raise(nameof(ActivityDetail));
            RevertAllCommand.RaiseCanExecuteChanged();
        }
    }

    public string OutstandingText => _outstandingCount == 1
        ? Strings.Get("app.managed.one")
        : Strings.Format("app.managed.many", _outstandingCount);

    // ------------------------------------------------------------------ actions

    public async Task InitialiseAsync()
    {
        IsBusy = true;

        // How long the setup checklist should stay up. Started while the reads run rather than
        // before them, and already finished for the ordinary case: a launch that fixed nothing.
        var linger = Task.CompletedTask;

        // The setup overlay hides the window until the backend is chosen, but the first catalog
        // read happens after it closes. Without a caption that gap is an unlabelled busy state
        // on a window the user is seeing for the first time.
        //
        // Given a longer fuse than anything else, because this is the one piece of work nobody
        // asked for. It takes about half a second now, and at the usual threshold that was long
        // enough to raise the indicator and then hold it up for its minimum -- so a window that
        // had finished loading went on saying it was loading for another half second.
        var activity = _activity.Begin(
            Strings.Get("activity.reading"), showAfter: TimeSpan.FromMilliseconds(700));
        try
        {
            if (_injectedBackend is not null)
            {
                // Tests supply a backend directly; there is nothing to bootstrap.
                _backend = _injectedBackend;
                IsSettingUp = false;
                ServiceReady = _injectedBackend.IsService;
            }
            else
            {
                // On the pool, not here. Bootstrapping creates folders, hashes and rewrites
                // profile files, asks the SCM about a service and reads the registry -- all of
                // it synchronous, and all of it ran inline on the UI thread of a window that
                // was already on screen. The steps report their progress through
                // ObservableObject, which marshals back to the UI thread on its own.
                var result = await Task.Run(() => _bootstrapper.RunAsync(_shutdown.Token), _shutdown.Token);

                _backend = result.Backend;
                ServiceReady = result.ServiceReady;
                ServiceNeedsRepair = result.ServiceNeedsRepair;
                SetupNotice = result.Notice ?? Bootstrapper.EnvironmentWarning();

                // Leave the checklist up briefly when it did real work, so a first-run user can
                // see what was installed rather than a panel that flashes past.
                //
                // Started rather than awaited. Awaited, it was 1.2 seconds of nothing happening
                // in front of somebody opening the app for the first time: the reads below did
                // not begin until it elapsed, so the pause and the loading were added together
                // instead of overlapping. Now the checklist comes down when what is behind it
                // is ready, and the pause costs nothing it did not already cost.
                linger = _bootstrapper.Steps.Any(step => step.Status == StepStatus.Fixed)
                    ? Task.Delay(TimeSpan.FromSeconds(1.2), _shutdown.Token)
                    : Task.CompletedTask;
            }

            IsServiceMode = _backend.IsService;
            CanApplyMachineScope = _backend.CanApplyMachineScope;
            ConnectionText = _backend.Description;
            ConnectionDetail = null;

            // The three reads are independent -- none is another's input -- so they run at the
            // same time rather than one after another. Each does its work on the pool and comes
            // back only to fill a bound collection, which is the part that must be on this
            // thread and the part that costs microseconds.
            var catalog = ReloadAsync();
            var startup = ReloadStartupAsync();
            var apps = ReloadAppsAsync();

            // Only the catalog is waited for, because the catalog is the tab in front of the
            // reader. The other two fill tabs nobody is looking at yet; holding the window's
            // first usable moment behind them bought nothing.
            await catalog;

            await linger;
            IsSettingUp = false;

            var count = _allTweaks.Count;
            Report(() => new ChangeSummary(
                Strings.Format("activity.loaded", count), null, IsProblem: false));

            // Started after the first read, so the loop never races the initial load. Left
            // unawaited on purpose: it runs for the lifetime of the window.
            _liveLoop ??= LiveLoopAsync();

            // Unawaited, and last. A failed or slow update check must never be something the
            // user waits through to reach a catalog that is already on screen.
            //
            // On the pool as well, for a reason that only shows up on a real machine: the
            // synchronous head of an HTTP request -- building the client, resolving the name,
            // negotiating TLS for the first time in a process -- runs inline on whichever
            // thread starts it, and measured here that was 450ms of the UI thread on a window
            // that had otherwise finished loading.
            //
            // Skipped when a backend was injected, which means a test: a unit test has no
            // business making an HTTP request to GitHub, and until this condition was here
            // every test that loaded the window made one.
            if (_injectedBackend is null && Settings.Current.IsCheckDue(DateTimeOffset.UtcNow))
                _ = Task.Run(LaunchUpdateCheckAsync, _shutdown.Token);

            // A tab that asked to be measured before there was anything to measure with.
            if (_cleanupWanted && Cleanup.Count == 0)
                await ReloadCleanupAsync();

            // Last, and only so that a failure in either surfaces rather than being dropped:
            // both have been filling their tabs since well before this line.
            await Task.WhenAll(startup, apps);
        }
        catch (Exception e)
        {
            IsSettingUp = false;
            SetStatus(Strings.Format("status.couldnotstart", e.Message), isError: true);
        }
        finally
        {
            IsBusy = false;
            await activity.DisposeAsync();
        }
    }

    /// <summary>
    /// Re-runs setup, overriding a previous refusal.
    ///
    /// Exists because the first answer to a UAC prompt is not always the final one: someone who
    /// clicked No on launch needs a way back that is not "reinstall the app".
    /// </summary>
    private async Task EnableServiceAsync()
    {
        IsBusy = true;
        var setup = _activity.Begin(Strings.Get("activity.settingupservice"));
        try
        {
            Bootstrapper.ClearDecline();

            if (_backend is not null)
                await _backend.DisposeAsync();
            _backend = null;

            _bootstrapper = new Bootstrapper { ForceServiceSetup = true };
            Raise(nameof(SetupSteps));
            IsSettingUp = true;

            var result = await _bootstrapper.RunAsync(_shutdown.Token);

            _backend = result.Backend;
            ServiceReady = result.ServiceReady;
            ServiceNeedsRepair = result.ServiceNeedsRepair;
            SetupNotice = result.Notice;
            IsServiceMode = result.Backend.IsService;
            CanApplyMachineScope = result.Backend.CanApplyMachineScope;
            ConnectionText = result.Backend.Description;
            IsSettingUp = false;

            await ReloadAsync();
            SetStatus(Strings.Get(result.ServiceReady
                ? "status.service.running"
                : "status.service.without"));
        }
        catch (Exception e)
        {
            IsSettingUp = false;
            SetStatus(e.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            await setup.DisposeAsync();
        }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        var activity = _activity.Begin(Strings.Get("activity.checking"));
        try
        {
            await ReloadAsync();
            SetStatus(Strings.Get("status.refreshed"));
        }
        catch (Exception e)
        {
            SetStatus(e.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            await activity.DisposeAsync();
        }
    }

    /// <param name="includeHistory">
    /// Whether to re-read the journal and the profile list as well as the catalog. False for
    /// the background loop, which runs often and would otherwise pay for two reads of things
    /// that only this app changes.
    /// </param>
    private async Task ReloadAsync(bool includeHistory = true)
    {
        if (_backend is null)
            return;

        var statuses = await _backend.GetStatusAsync(_shutdown.Token);

        // Update in place where possible so the selection and scroll position survive a refresh.
        var existing = _allTweaks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var rebuilt = new List<TweakItemViewModel>(statuses.Count);

        foreach (var status in statuses)
        {
            if (existing.TryGetValue(status.Tweak.Id, out var item))
            {
                item.Status = status;
                rebuilt.Add(item);
            }
            else
            {
                var created = new TweakItemViewModel(status);
                created.SelectionChanged += OnChoiceSelectionChanged;
                rebuilt.Add(created);
            }
        }

        _allTweaks.Clear();
        _allTweaks.AddRange(rebuilt);

        foreach (var tweak in _allTweaks)
            tweak.MachineScopeAvailable = _canApplyMachineScope;

        SyncCategories();
        SyncUpdateTweaks();
        ApplyFilter();

        OutstandingCount = _allTweaks.Count(t => t.IsManaged);

        // The bulk read above uses default selections, because a single call has nowhere to
        // carry per-tweak choices. Anything the user has chosen differently is re-read here, or
        // its badge would flip back to reflect an option they did not pick.
        //
        // Only what they have actually changed. Fifty of the eighty-nine tweaks carry choices,
        // and re-reading all of them asked the same question twice: on a row still on its
        // defaults the bulk read had already resolved to exactly those defaults. That was fifty
        // extra calls per refresh -- fifty round trips over the pipe on an installed copy, four
        // times a minute, for answers already in hand. A machine where nothing has been
        // customised now makes none of them.
        var customised = _allTweaks.Where(t => t.HasChoices && !t.UsesDefaultChoices).ToList();
        foreach (var tweak in customised)
            await RefreshTweakAsync(tweak);

        // The journal and the profile list only change when something acts on them, so they are
        // refreshed after an operation and on an explicit Refresh rather than on every tick of
        // the background loop. Over the pipe that is the difference between two round trips
        // every five seconds and four.
        if (includeHistory)
        {
            await ReloadJournalAsync();
            await ReloadProfilesAsync();
        }

        _lastUpdatedUtc = DateTimeOffset.UtcNow;
        Raise(nameof(LastUpdatedText));
    }

    /// <summary>
    /// Re-reads the machine every few seconds for as long as the window is open.
    ///
    /// Deliberately not a "refresh" the user has to remember to press. These values are changed
    /// by things other than this app -- Windows Update resets several of them, the service's
    /// reconciler re-applies drifted ones, and Settings can change any of the user-scoped ones
    /// while the window is open -- so a display that is only correct straight after you clicked
    /// something is a display that quietly lies for the rest of the session.
    ///
    /// Cheap because the read runs on the thread pool and the list reconciles in place: a tick
    /// that finds nothing changed mutates no bound collection and repaints nothing.
    /// </summary>
    private async Task LiveLoopAsync()
    {
        using var timer = new PeriodicTimer(LiveInterval);
        IsLive = true;

        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(true))
            {
                // The app has been removed from the machine. There is nothing left to read, and
                // a read would re-create the data folder that was just deleted.
                if (_detached)
                    break;

                // The age caption ticks even when a read is skipped, so a loop that has stalled
                // shows itself as stale rather than as fresh.
                Raise(nameof(LastUpdatedText));

                // Never while the user's own operation is in flight: re-reading mid-apply would
                // race the change and could report a half-applied tweak as its final state.
                if (IsBusy || _activity.IsRunning || _allTweaks.Any(t => t.IsBusy))
                    continue;

                try
                {
                    await ReloadAsync(includeHistory: false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    // A background read that fails must not shout. The service being restarted
                    // under us is the common case, and the next tick usually recovers.
                    System.Diagnostics.Debug.WriteLine(e);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLive = false;
        }
    }

    private void OnChoiceSelectionChanged(TweakItemViewModel tweak)
    {
        // Fired from a radio button, so it cannot be awaited. RefreshTweakAsync swallows its
        // own failures precisely so this cannot become an unobserved exception.
        _ = RefreshTweakAsync(tweak);
    }

    /// <summary>Re-reads one tweak under its current choices, leaving the rest of the list alone.</summary>
    private async Task RefreshTweakAsync(TweakItemViewModel tweak)
    {
        if (_backend is null)
            return;

        try
        {
            var status = await _backend.GetStatusAsync(tweak.Id, tweak.SelectedOptions, TargetFor(tweak), _shutdown.Token);
            if (status is not null)
                tweak.Status = status;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            SetStatus(e.Message, isError: true);
        }
    }

    /// <summary>
    /// Reconciles the category list in place.
    ///
    /// Deliberately never calls Clear(): emptying a collection bound to ListBox.SelectedItem
    /// drives the selection to null and the control cannot recover it, which both wipes the
    /// user's filter and leaves nothing highlighted. Adding and removing individual entries
    /// leaves an unaffected selection alone.
    /// </summary>
    /// <summary>
    /// Rebuilds the Windows Update tab's list from the catalog.
    ///
    /// Cleared and refilled rather than diffed, unlike the Tweaks list: this one has no
    /// selection and no scroll position worth preserving at a dozen rows, and the items
    /// themselves are shared with the Tweaks tab, so nothing about a row is lost by dropping
    /// the reference to it here.
    /// </summary>
    /// <summary>
    /// Rebuilds the Windows Update tab's list, in place.
    ///
    /// Reconciled rather than emptied and refilled, for the same reason the catalog list is:
    /// this runs on every background refresh, and a bound collection that clears itself first
    /// makes the list drop its scroll position and its selection on every tick. The rows are
    /// the same objects the catalog holds, so in the ordinary case nothing here changes at all.
    /// </summary>
    private void SyncUpdateTweaks()
    {
        var wanted = _allTweaks.Where(t => t.IsWindowsUpdate)
            .OrderBy(t => t.Risk)
            .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = UpdateTweaks.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(UpdateTweaks[i]))
                UpdateTweaks.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= UpdateTweaks.Count)
                UpdateTweaks.Add(wanted[i]);
            else if (!ReferenceEquals(UpdateTweaks[i], wanted[i]))
                UpdateTweaks.Insert(i, wanted[i]);
        }

        Raise(nameof(UpdateSummary));
    }

    private void SyncCategories()
    {
        var desired = CatalogFilter.CategoriesFor(_allTweaks);

        for (var i = Categories.Count - 1; i >= 1; i--)
        {
            if (!desired.Contains(Categories[i], StringComparer.OrdinalIgnoreCase))
                Categories.RemoveAt(i);
        }

        foreach (var category in desired)
        {
            if (!Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                Categories.Add(category);
        }
    }

    private void ApplyFilter()
    {
        var filtered = CatalogFilter.Select(_allTweaks, _selectedCategory, _searchText);

        // Reconciled in place rather than Clear()-and-refill. The background refresh runs every
        // few seconds, and a collection that empties itself first makes the ListBox drop its
        // scroll position and its selection on every tick -- the list would jump under the
        // cursor while you were reading it. Same reasoning as SyncCategories.
        for (var i = Tweaks.Count - 1; i >= 0; i--)
        {
            if (!filtered.Contains(Tweaks[i]))
                Tweaks.RemoveAt(i);
        }

        for (var i = 0; i < filtered.Count; i++)
        {
            if (i >= Tweaks.Count)
                Tweaks.Add(filtered[i]);
            else if (!ReferenceEquals(Tweaks[i], filtered[i]))
                Tweaks.Insert(i, filtered[i]);
        }

        if (SelectedTweak is not null && !filtered.Contains(SelectedTweak))
            SelectedTweak = null;
    }

    private async Task ReloadJournalAsync()
    {
        if (_backend is null)
            return;

        RebuildJournal(await _backend.GetJournalAsync(80, _shutdown.Token));
    }

    /// <summary>
    /// Turns journal lines into rows.
    ///
    /// Split out from the read so that a language change can rebuild the rows from the lines
    /// already in memory. The rows are plain objects with no change notification, which is
    /// right for a list of things that happened: nothing about a past change can change.
    /// </summary>
    private void RebuildJournal(IReadOnlyList<JournalLine> lines)
    {
        // Titles come from the loaded catalog. A tweak that has since been removed still has
        // journal entries, and falling back to the id keeps those rows readable rather than
        // blank -- a record of a change you can no longer name is worse than an ugly name.
        var titles = _allTweaks.ToDictionary(t => t.Id, t => t.Title, StringComparer.OrdinalIgnoreCase);
        var built = JournalEntryViewModel.Build(
            lines, id => titles.TryGetValue(id, out var title) ? title : id);

        Journal.Clear();
        foreach (var entry in built)
            Journal.Add(entry);

        Raise(nameof(HasJournal));
        UnfinishedCount = built.Count(e => e.IsUnfinished);
    }

    /// <summary>
    /// Whether anything has ever been changed on this PC by this program.
    ///
    /// Exists so the History tab can say that nothing has, rather than showing an empty panel.
    /// A blank page is indistinguishable from a page that failed to load, and on a tool whose
    /// whole claim is that it keeps a record, "the record is empty" and "the record is missing"
    /// are very different things to leave a reader guessing between.
    /// </summary>
    public bool HasJournal => Journal.Count > 0;

    /// <summary>
    /// Changes that were started and never confirmed finished, normally a crash or a power cut
    /// mid-apply. Surfaced because it is the one journal state that asks the user to act.
    /// </summary>
    public int UnfinishedCount
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                Raise(nameof(HasUnfinished));
        }
    }

    public bool HasUnfinished => UnfinishedCount > 0;

    /// <summary>
    /// The loaded catalog row for an id, or null when this build has no such tweak.
    ///
    /// Handed to each profile card so it can name what it would apply. A profile carries ids
    /// and nothing else; every word next to one -- title, category, risk -- belongs to the
    /// catalog, and the catalog is what gets translated.
    /// </summary>
    private TweakItemViewModel? FindTweak(string id)
        => _allTweaks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private async Task ReloadProfilesAsync()
    {
        if (_backend is null)
            return;

        var loaded = await _backend.GetProfilesAsync(_shutdown.Token);

        // Reconciled in place rather than cleared and rebuilt, the same way the tweak list is.
        // Rebuilding kept which cards were open by copying a set of names across, which worked
        // while a card was inert data; it does not work now that a card can be mid-apply, because
        // the live loop ticks every few seconds and would replace the object the progress
        // reports are being delivered to.
        var existing = Profiles.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        for (var i = Profiles.Count - 1; i >= 0; i--)
        {
            if (!loaded.Any(l => string.Equals(l.Name, Profiles[i].Name, StringComparison.OrdinalIgnoreCase)))
                Profiles.RemoveAt(i);
        }

        for (var i = 0; i < loaded.Count; i++)
        {
            if (existing.TryGetValue(loaded[i].Name, out var card))
            {
                // The rows hold what the machine said when they were built, so they are thrown
                // away and rebuilt on the next read. Not while the card is being applied: that
                // would clear the ticks out from under somebody watching them appear.
                if (!card.IsApplying)
                    card.Invalidate();

                if (Profiles.IndexOf(card) != i)
                {
                    Profiles.Remove(card);
                    Profiles.Insert(i, card);
                }

                continue;
            }

            Profiles.Insert(i, new ProfileViewModel(loaded[i], FindTweak));
        }
    }

    // ------------------------------------------------------------------ updates

    /// <summary>The release on offer, or null when this is the newest build.</summary>
    public bool UpdateAvailable => _update is not null;

    public string UpdateTitle => _update is null
        ? ""
        : Strings.Format("update.available.title", _update.Version.ToString(3));

    /// <summary>
    /// The line under the banner headline.
    ///
    /// It says which version you are on and nothing else. It used to carry the first line of
    /// the release notes as well, which sounds useful and is not: release notes are markdown
    /// written for a changelog, so what landed in the banner was whatever sentence happened to
    /// come first, truncated mid-clause. The one fact the banner needs to supply is the one
    /// the headline does not already give -- what you are upgrading from.
    /// </summary>
    public string UpdateDetail => _update is null
        ? ""
        : Strings.Format("update.available.detail", UpdateClient.CurrentVersion().ToString(3));

    /// <summary>
    /// The launch-time check, run only when the user's cadence says it is due.
    ///
    /// Whether it succeeded or not, the attempt is recorded: a machine that is offline for a
    /// week must not ask on every single launch, because that is precisely the machine least
    /// able to answer.
    /// </summary>
    private async Task LaunchUpdateCheckAsync()
    {
        await CheckForUpdatesAsync(_shutdown.Token).ConfigureAwait(true);
        Settings.RecordCheck();
    }

    /// <summary>
    /// Asks GitHub whether there is a newer release. Nothing is downloaded here.
    ///
    /// Checking on launch and installing on a click are deliberately separate. The whole premise
    /// of this program is that nothing happens to the machine unless somebody asked for it, and
    /// an updater that fetched and swapped code on its own would be the one exception -- on the
    /// component that runs as LocalSystem, of all of them.
    /// </summary>
    /// <returns>
    /// What to tell somebody who asked for this check. On launch nobody did, and the caller
    /// throws it away: a failed check is not news when it was nobody's idea. In Settings it is
    /// the whole point of having pressed the button.
    /// </returns>
    public async Task<string> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, ct);

        try
        {
            using var client = new UpdateClient();
            var status = await client.CheckAsync(linked.Token).ConfigureAwait(true);

            if (status.Problem is { } problem)
                return Strings.Format("update.checkfailed", problem);

            if (!status.UpdateAvailable)
                return Strings.Format("update.newest", status.Current.ToString(3));

            _update = status.Latest;
            Raise(nameof(UpdateAvailable));
            Raise(nameof(UpdateTitle));
            Raise(nameof(UpdateDetail));
            InstallUpdateCommand.RaiseCanExecuteChanged();

            return Strings.Format("update.available.status", _update!.Version.ToString(3));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Strings.Format("update.checkfailed", e.Message);
        }
    }

    // ------------------------------------------------------------ ISettingsHost

    /// <summary>
    /// Undoes everything, for the removal path.
    ///
    /// Deliberately the same backend call the Revert everything button makes. An uninstaller
    /// with its own idea of how to put settings back is an uninstaller whose bugs nobody finds
    /// until the day somebody uses it.
    /// </summary>
    public async Task<int> RevertEverythingAsync(CancellationToken ct = default)
    {
        if (_backend is null)
            return 0;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, ct);

        var results = await _backend.RevertAllAsync(linked.Token).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
        return results.Count;
    }

    /// <summary>
    /// Stops talking to the machine, permanently.
    ///
    /// Called between "undo everything" and "delete everything". The pipe has to be closed
    /// before the service is stopped, and the refresh loop has to stop before the data folder
    /// is deleted -- a tick landing afterwards would re-create the folder that was just removed
    /// and leave the app having lied about the machine being clean.
    /// </summary>
    public async Task DetachAsync()
    {
        _detached = true;

        if (_backend is not null)
        {
            await _backend.DisposeAsync().ConfigureAwait(true);
            _backend = null;
        }

        IsLive = false;
        ConnectionText = Strings.Get("connection.removed");
        OutstandingCount = 0;

        RefreshCommand.RaiseCanExecuteChanged();
        RevertAllCommand.RaiseCanExecuteChanged();
        EnableServiceCommand.RaiseCanExecuteChanged();
    }

    private async Task InstallUpdateAsync()
    {
        if (_update is null)
            return;

        IsBusy = true;
        await using var activity = _activity.Begin(
            Strings.Format("update.downloading", _update.Version.ToString(3)));

        try
        {
            using var client = new UpdateClient();
            var progress = new Progress<double>(fraction =>
                activity.Describe(
                    Strings.Format("update.downloading.percent", (int)(fraction * 100))));

            var outcome = await new UpdateInstaller()
                .ApplyAsync(client, _update, progress, _shutdown.Token)
                .ConfigureAwait(true);

            SetStatus(outcome.Message, isError: !outcome.Applied);

            if (outcome.Applied)
            {
                // The banner has to go even though the running process is still the old build:
                // leaving it up invites a second download of something already installed.
                _update = null;
                Raise(nameof(UpdateAvailable));
                InstallUpdateCommand.RaiseCanExecuteChanged();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SetStatus(Strings.Format("update.failed", e.Message), isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ApplyAsync(TweakItemViewModel? tweak) => RunChangeAsync(tweak, "activity.applying", async backend =>
        await backend.ApplyAsync(tweak!.Id, tweak.SelectedOptions, target: TargetFor(tweak), ct: _shutdown.Token));

    private Task DryRunAsync(TweakItemViewModel? tweak) => RunChangeAsync(tweak, "activity.dryrun", async backend =>
        await backend.ApplyAsync(tweak!.Id, tweak.SelectedOptions, dryRun: true, target: TargetFor(tweak), ct: _shutdown.Token));

    private Task RevertAsync(TweakItemViewModel? tweak) => RunChangeAsync(tweak, "activity.reverting.one", async backend =>
        await backend.RevertAsync(tweak!.Id, _shutdown.Token));

    private async Task RunChangeAsync(
        TweakItemViewModel? tweak,
        string captionKey,
        Func<IOptimizerBackend, Task<IReadOnlyList<ChangeResult>>> operation)
    {
        if (tweak is null || _backend is null || tweak.IsBusy)
            return;

        // Refused here, where the answer has been on the row since it was drawn. The engine
        // refuses this too, and its sentence -- "requires elevation; run the service or start
        // the CLI as administrator" -- is written for somebody at a command line. Sending a
        // change down a pipe in order to be told what the window already knew also spends a
        // round trip to produce a worse sentence.
        if (tweak.NeedsAdmin)
        {
            Report(new ChangeSummary(AdminNeeded.Status, tweak.AdminNote, IsProblem: true));
            return;
        }

        tweak.IsBusy = true;
        // The key rather than a verb: German does not put the verb where English does, so
        // the whole caption has to come out of the table as one sentence.
        var activity = _activity.Begin(Strings.Format(captionKey, tweak.Title));
        try
        {
            var results = await operation(_backend);

            // One tweak, one result: name it and say what it means. A batch summary here would
            // read "1 change made" about a row the user is looking straight at.
            Report(results.Count == 1
                ? ChangeSummary.ForOne(results[0], tweak.Title)
                : ChangeSummary.ForMany(results, tweak.Title));

            // Re-read rather than assuming: the point of Verify is that an apply which reports
            // success can still not have landed.
            activity.Describe(Strings.Get("activity.confirming"));
            await ReloadAsync();
        }
        catch (Exception e)
        {
            SetStatus(e.Message, isError: true);
        }
        finally
        {
            tweak.IsBusy = false;
            await activity.DisposeAsync();
        }
    }

    private async Task RevertAllAsync()
    {
        if (_backend is null)
            return;

        IsBusy = true;
        var activity = _activity.Begin(Strings.Format("activity.reverting", OutstandingCount));
        try
        {
            var results = await _backend.RevertAllAsync(_shutdown.Token);
            Report(results.Count == 0
                ? new ChangeSummary(
                    Strings.Get("activity.nothing.title"),
                    Strings.Get("activity.nothing.detail"),
                    IsProblem: false)
                : ChangeSummary.ForMany(results, Strings.Get("activity.everything")));

            await ReloadAsync();
        }
        catch (Exception e)
        {
            SetStatus(e.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
            await activity.DisposeAsync();
        }
    }

    /// <summary>
    /// Applies a profile, showing the card being worked through rather than a spinner over a
    /// frozen list.
    ///
    /// Forty-two tweaks is fifteen seconds of nothing moving, and "is it stuck, or is it
    /// working?" is the only question a reader has during it. The reports come from inside the
    /// loop that does the work -- see <see cref="BatchProgress"/> -- so what is on screen is
    /// where the batch actually is, and it stops on the tweak that stops rather than animating
    /// cheerfully past it.
    /// </summary>
    private async Task ApplyProfileAsync(ProfileViewModel? card)
    {
        if (card is null || _backend is null)
            return;

        IsBusy = true;
        card.BeginRun();

        var activity = _activity.Begin(
            Strings.Format("activity.applying.profile", card.Name, card.Summary.TweakCount));
        try
        {
            var results = await _backend.ApplyProfileAsync(
                card.Name,
                progress =>
                {
                    card.Report(progress);

                    // The activity panel names the tweak too, so the answer is on screen even
                    // for somebody who is looking at a different tab. Only on the way in: naming
                    // it again on the way out would make the caption flicker twice per tweak.
                    if (progress.IsRunning && TitleOf(card, progress.TweakId) is { } title)
                        activity.Describe(Strings.Format("activity.applying.one", title));

                    return Task.CompletedTask;
                },
                _shutdown.Token);

            Report(ChangeSummary.ForMany(results, Strings.Format("activity.preset.named", card.Name)));

            await ReloadAsync();
        }
        catch (Exception e)
        {
            SetStatus(e.Message, isError: true);
        }
        finally
        {
            card.EndRun();
            IsBusy = false;
            await activity.DisposeAsync();
        }
    }

    /// <summary>The translated title of one of a card's rows, or null when it has no such row.</summary>
    private static string? TitleOf(ProfileViewModel card, string tweakId)
        => card.Tweaks.FirstOrDefault(t =>
            string.Equals(t.Id, tweakId, StringComparison.OrdinalIgnoreCase))?.Title;

    private static bool IsSuccess(Outcome outcome)
        => outcome is not (Outcome.Failed or Outcome.RolledBack);

    private void SetStatus(string message, bool isError = false)
        => Report(new ChangeSummary(message, null, isError));

    /// <summary>
    /// Puts a summary into the activity panel: headline on top, meaning underneath.
    ///
    /// The factory is kept, not just the text it produced. The panel holds the last thing that
    /// happened, which can be minutes old by the time somebody changes the language, and a line
    /// left in the old language is the one thing on screen that would not have moved. Re-running
    /// the closure re-renders it from the same facts in the new language.
    /// </summary>
    private void Report(ChangeSummary summary) => Report(() => summary);

    private void Report(Func<ChangeSummary> summarise)
    {
        _lastSummary = summarise;
        var summary = summarise();

        StatusMessage = summary.Headline;
        StatusDetail = summary.Detail;
        StatusIsError = summary.IsProblem;

        Raise(nameof(ActivityTitle));
        Raise(nameof(ActivityDetail));
    }

    /// <summary>How to re-render the standing status line. Null until something has happened.</summary>
    private Func<ChangeSummary>? _lastSummary;

    public void Dispose()
    {
        Strings.LanguageChanged -= OnLanguageChanged;
        _shutdown.Cancel();

        // The live loop observes the same token, so cancelling is enough to end it. It is not
        // waited on: it can be parked inside a backend read, and blocking the shutdown path on
        // a pipe round-trip is how a window ends up refusing to close.
        _liveLoop = null;

        _backend?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _shutdown.Dispose();
    }
}
