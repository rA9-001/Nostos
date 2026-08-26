using System.Collections.ObjectModel;
using Nostos.App.Backends;
using Nostos.App.Startup;
using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Updates;
using Nostos.Win32.Updates;
using Nostos.Core.Engine;
using Nostos.Ipc;

namespace Nostos.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly List<TweakItemViewModel> _allTweaks = [];
    private readonly CancellationTokenSource _shutdown = new();
    private ReleaseInfo? _update;

    private readonly IOptimizerBackend? _injectedBackend;

    private IOptimizerBackend? _backend;
    private string _connectionText = "connecting…";
    private string? _connectionDetail;
    private bool _isServiceMode;
    private bool _canApplyMachineScope = true;
    private bool _isBusy;
    private string _statusMessage = "";
    private bool _statusIsError;
    private string? _searchText;
    private string _selectedCategory = AllCategories;
    private TweakItemViewModel? _selectedTweak;
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

    /// <summary>
    /// How often the catalog re-reads itself when nothing else is happening.
    ///
    /// The machine is not the only thing that writes these values: Windows Update resets
    /// several of them, the service's reconciler re-applies drifted ones, and the user can
    /// change any of them in Settings while the window is open. A view that is only correct
    /// immediately after you clicked something is a view you cannot trust.
    /// </summary>
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(5);

    public const string AllCategories = "all";

    /// <summary>
    /// A filter, not a category.
    ///
    /// Whether a tweak applies here is live machine state -- a service that is not installed, a
    /// Windows build that never had the setting, no game running to point at -- and it differs
    /// between two PCs holding the same catalog. Making it a real <see cref="TweakCategory"/>
    /// would mean a docs page could not name the category it is filed under, a profile could
    /// reference a bucket that is empty on the next machine, and CI could not check either. So
    /// it lives here, alongside "all", derived from what the last refresh actually found.
    /// </summary>
    public const string NotApplicableCategory = "not-applicable";

    /// <summary>The band printed above the unavailable rows, and shown when they are filtered to.</summary>
    public const string NotApplicableHeader = "Not applicable on this PC";

    private const string NotApplicableDescription =
        "These need something this PC does not have: a service that is not installed, a Windows "
        + "version that never had the setting, or a running game to point at. They are listed so "
        + "you can see they exist, and they all read OFF because none of them can be switched on.";

    /// <param name="backend">
    /// Supplied by tests. When null the view model discovers a backend for itself, preferring
    /// the service and falling back to the in-process engine.
    /// </param>
    /// <param name="activity">
    /// Supplied by tests so the show-delay and minimum-visible thresholds can be driven without
    /// sleeping. Production uses the real clock.
    /// </param>
    public MainWindowViewModel(IOptimizerBackend? backend = null, ActivityTracker? activity = null)
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

        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        InstallUpdateCommand = new AsyncCommand(InstallUpdateAsync, () => _update is not null && !IsBusy);
        RevertAllCommand = new AsyncCommand(RevertAllAsync, () => !IsBusy && OutstandingCount > 0);
        ApplyCommand = new AsyncCommand(p => ApplyAsync(p as TweakItemViewModel));
        RevertCommand = new AsyncCommand(p => RevertAsync(p as TweakItemViewModel));
        DryRunCommand = new AsyncCommand(p => DryRunAsync(p as TweakItemViewModel));
        ApplyProfileCommand = new AsyncCommand(p => ApplyProfileAsync(p as ProfileSummary));
        EnableServiceCommand = new AsyncCommand(
            EnableServiceAsync,
            () => !AppPaths.IsPortable && (!_serviceReady || _serviceNeedsRepair) && !IsBusy);
    }

    // ------------------------------------------------------------- collections

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategories];
    public ObservableCollection<JournalEntryViewModel> Journal { get; } = [];
    public ObservableCollection<ProfileSummary> Profiles { get; } = [];

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
    public AsyncCommand EnableServiceCommand { get; }

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
        !_serviceReady ? "Running without the background service"
        : _serviceNeedsRepair ? "The background service needs repairing"
        : "Worth knowing";

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
    public string SetupActionLabel => _serviceReady ? "Repair" : "Enable";

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
    /// Collapsing the two meant the outcome of an apply was overwritten by "working…" the next
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
        ? _activity.Caption ?? "Working…"
        : string.IsNullOrWhiteSpace(StatusMessage) ? "Ready" : StatusMessage;

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
                return "Your previous settings are saved before anything is changed.";

            if (!string.IsNullOrWhiteSpace(StatusDetail))
                return StatusDetail;

            return OutstandingCount == 0
                ? "Nothing has been changed on this PC by this program."
                : OutstandingCount == 1
                    ? "1 change made by this program. Revert everything puts it back."
                    : $"{OutstandingCount} changes made by this program. Revert everything puts them back.";
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
                return "not read yet";

            var age = DateTimeOffset.UtcNow - at;
            return age.TotalSeconds switch
            {
                < 3 => "live",
                < 60 => $"updated {(int)age.TotalSeconds}s ago",
                < 3600 => $"updated {(int)age.TotalMinutes}m ago",
                _ => "stale",
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
    public string? SelectedCategoryPromise => _selectedCategory switch
    {
        AllCategories => null,
        NotApplicableCategory => NotApplicableDescription,
        _ => TweakCategories.Find(_selectedCategory)?.Promise,
    };

    public TweakItemViewModel? SelectedTweak
    {
        get => _selectedTweak;
        set
        {
            if (SetField(ref _selectedTweak, value))
                Raise(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selectedTweak is not null;

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
        ? "1 change managed by this app"
        : $"{_outstandingCount} changes managed by this app";

    // ------------------------------------------------------------------ actions

    public async Task InitialiseAsync()
    {
        IsBusy = true;

        // The setup overlay hides the window until the backend is chosen, but the first catalog
        // read happens after it closes. Without a caption that gap is an unlabelled busy state
        // on a window the user is seeing for the first time.
        var activity = _activity.Begin("Reading your PC's current settings…");
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
                var result = await _bootstrapper.RunAsync(_shutdown.Token);

                _backend = result.Backend;
                ServiceReady = result.ServiceReady;
                ServiceNeedsRepair = result.ServiceNeedsRepair;
                SetupNotice = result.Notice ?? Bootstrapper.EnvironmentWarning();

                // Leave the checklist up briefly when it did real work, so a first-run user can
                // see what was installed rather than a panel that flashes past.
                if (_bootstrapper.Steps.Any(step => step.Status == StepStatus.Fixed))
                    await Task.Delay(TimeSpan.FromSeconds(1.2), _shutdown.Token);

                IsSettingUp = false;
            }

            IsServiceMode = _backend.IsService;
            CanApplyMachineScope = _backend.CanApplyMachineScope;
            ConnectionText = _backend.Description;
            ConnectionDetail = null;

            await ReloadAsync();
            SetStatus($"{_allTweaks.Count} tweaks loaded");

            // Started after the first read, so the loop never races the initial load. Left
            // unawaited on purpose: it runs for the lifetime of the window.
            _liveLoop ??= LiveLoopAsync();

            // Unawaited, and last. A failed or slow update check must never be something the
            // user waits through to reach a catalog that is already on screen.
            _ = CheckForUpdateAsync();
        }
        catch (Exception e)
        {
            IsSettingUp = false;
            SetStatus($"could not start: {e.Message}", isError: true);
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
        var setup = _activity.Begin("Setting up the background service…");
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
            SetStatus(result.ServiceReady
                ? "background service is running"
                : "continuing without the background service");
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
        var activity = _activity.Begin("Checking your PC's current settings…");
        try
        {
            await ReloadAsync();
            SetStatus("refreshed");
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

        SyncCategories();
        ApplyFilter();

        OutstandingCount = _allTweaks.Count(t => t.IsManaged);

        // The bulk read above uses default selections, because a single call has nowhere to
        // carry per-tweak choices. Anything the user has chosen differently is re-read here, or
        // its badge would flip back to reflect an option they did not pick.
        foreach (var tweak in _allTweaks.Where(t => t.HasChoices))
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
            var status = await _backend.GetStatusAsync(tweak.Id, tweak.SelectedOptions, _shutdown.Token);
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
    /// Which band a row sits under: its half of the catalog, or the unavailable pile.
    ///
    /// Applicability wins over the group. A tweak that cannot run here is not a Gaming change
    /// or a Windows change from the reader's point of view -- it is not a change at all.
    /// </summary>
    private static (string Header, string Description) SectionOf(TweakItemViewModel tweak)
    {
        if (!tweak.IsApplicable)
            return (NotApplicableHeader, NotApplicableDescription);

        var group = TweakCategories.GroupOf(tweak.Category);
        return (TweakCategories.NameOfGroup(group), TweakCategories.DescriptionOfGroup(group));
    }

    /// <summary>
    /// Reconciles the category list in place.
    ///
    /// Deliberately never calls Clear(): emptying a collection bound to ListBox.SelectedItem
    /// drives the selection to null and the control cannot recover it, which both wipes the
    /// user's filter and leaves nothing highlighted. Adding and removing individual entries
    /// leaves an unaffected selection alone.
    /// </summary>
    private void SyncCategories()
    {
        var desired = _allTweaks
            .Select(t => t.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Group first, so the sidebar reads Gaming-then-Windows and the band headings the
            // item template draws land where they belong.
            .OrderBy(TweakCategories.GroupOf)
            .ThenBy(TweakCategories.OrderOf)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        // Offered only when there is something in it. An always-present filter that is usually
        // empty trains people to ignore it, and on a machine where everything applies it is a
        // question with no answer.
        if (_allTweaks.Any(t => !t.IsApplicable))
            desired.Add(NotApplicableCategory);

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
        // Nothing is filtered out by how well evidenced it is. Every tweak in the catalog is
        // listed, always; a user who had heard of one and could not find it concluded the tool
        // lacked it, and went looking for a .reg file instead.
        //
        // Group first, so the Gaming half is never interleaved with the Windows half. Turning
        // off the Fax service and extending the GPU timeout are both reasonable things to do
        // and have nothing to do with each other, and a single flat list said otherwise.
        var filtered = _allTweaks
            .Where(t => _selectedCategory switch
            {
                AllCategories => true,
                NotApplicableCategory => !t.IsApplicable,
                _ => string.Equals(t.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase),
            })
            .Where(t => t.Matches(_searchText))
            // Unavailable rows sink to the bottom whatever else is selected. They are not
            // choices, and leaving them interleaved means every scan of the list steps over
            // things that cannot be clicked.
            .OrderBy(t => t.IsApplicable ? 0 : 1)
            .ThenBy(t => TweakCategories.GroupOf(t.Category))
            .ThenBy(t => TweakCategories.OrderOf(t.Category))
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        // The band goes on the first row of each section, which is only knowable once the
        // filter has run. Cleared on every other row, or a row that used to lead a section
        // keeps its heading after something above it appears.
        string? previousSection = null;
        foreach (var tweak in filtered)
        {
            var (header, description) = SectionOf(tweak);
            var leads = header != previousSection;

            tweak.GroupHeader = leads ? header : null;
            tweak.GroupDescription = leads ? description : null;
            previousSection = header;
        }

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

        var lines = await _backend.GetJournalAsync(80, _shutdown.Token);

        // Titles come from the loaded catalog. A tweak that has since been removed still has
        // journal entries, and falling back to the id keeps those rows readable rather than
        // blank -- a record of a change you can no longer name is worse than an ugly name.
        var titles = _allTweaks.ToDictionary(t => t.Id, t => t.Title, StringComparer.OrdinalIgnoreCase);
        var built = JournalEntryViewModel.Build(
            lines, id => titles.TryGetValue(id, out var title) ? title : id);

        Journal.Clear();
        foreach (var entry in built)
            Journal.Add(entry);

        UnfinishedCount = built.Count(e => e.IsUnfinished);
    }

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

    private async Task ReloadProfilesAsync()
    {
        if (_backend is null)
            return;

        Profiles.Clear();
        foreach (var profile in await _backend.GetProfilesAsync(_shutdown.Token))
            Profiles.Add(profile);
    }

    // ------------------------------------------------------------------ updates

    /// <summary>The release on offer, or null when this is the newest build.</summary>
    public bool UpdateAvailable => _update is not null;

    public string UpdateTitle => _update is null
        ? ""
        : $"Nostos {_update.Version.ToString(3)} is available";

    public string UpdateDetail => _update is null
        ? ""
        : $"You have {UpdateClient.CurrentVersion().ToString(3)}. " + FirstLineOf(_update.Notes);

    /// <summary>
    /// The first meaningful line of the release notes, for the banner.
    ///
    /// Release notes are markdown and start with a heading often enough that showing the raw
    /// first line would put "## Changes since v0.2.0" in front of the user.
    /// </summary>
    private static string FirstLineOf(string notes)
    {
        foreach (var line in notes.Split('\n'))
        {
            var trimmed = line.Trim().TrimStart('#', '-', '*', ' ');
            if (trimmed.Length > 0 && !trimmed.StartsWith('|') && !trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed.Length > 120 ? trimmed[..117] + "..." : trimmed;
        }

        return "";
    }

    /// <summary>
    /// Asks GitHub whether there is a newer release. Nothing is downloaded here.
    ///
    /// Checking on launch and installing on a click are deliberately separate. The whole premise
    /// of this program is that nothing happens to the machine unless somebody asked for it, and
    /// an updater that fetched and swapped code on its own would be the one exception -- on the
    /// component that runs as LocalSystem, of all of them.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            using var client = new UpdateClient();
            var status = await client.CheckAsync(_shutdown.Token).ConfigureAwait(true);

            // A failed check is not news. Offline, a captive portal or a rate limit are all
            // normal, and none of them is something the user can act on.
            if (!status.UpdateAvailable)
                return;

            _update = status.Latest;
            Raise(nameof(UpdateAvailable));
            Raise(nameof(UpdateTitle));
            Raise(nameof(UpdateDetail));
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_update is null)
            return;

        IsBusy = true;
        await using var activity = _activity.Begin($"Downloading Nostos {_update.Version.ToString(3)}…");

        try
        {
            using var client = new UpdateClient();
            var progress = new Progress<double>(fraction =>
                activity.Describe($"Downloading… {(int)(fraction * 100)}%"));

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
            SetStatus($"Update failed: {e.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ApplyAsync(TweakItemViewModel? tweak) => RunChangeAsync(tweak, "Applying", async backend =>
        await backend.ApplyAsync(tweak!.Id, tweak.SelectedOptions, ct: _shutdown.Token));

    private Task DryRunAsync(TweakItemViewModel? tweak) => RunChangeAsync(tweak, "Checking", async backend =>
        await backend.ApplyAsync(tweak!.Id, tweak.SelectedOptions, dryRun: true, ct: _shutdown.Token));

    private Task RevertAsync(TweakItemViewModel? tweak) => RunChangeAsync(tweak, "Reverting", async backend =>
        await backend.RevertAsync(tweak!.Id, _shutdown.Token));

    private async Task RunChangeAsync(
        TweakItemViewModel? tweak,
        string verb,
        Func<IOptimizerBackend, Task<IReadOnlyList<ChangeResult>>> operation)
    {
        if (tweak is null || _backend is null || tweak.IsBusy)
            return;

        tweak.IsBusy = true;
        var activity = _activity.Begin($"{verb} {tweak.Title}…");
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
            activity.Describe("Checking what actually changed…");
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
        var activity = _activity.Begin($"Reverting {OutstandingCount} change(s)…");
        try
        {
            var results = await _backend.RevertAllAsync(_shutdown.Token);
            Report(results.Count == 0
                ? new ChangeSummary(
                    "Nothing to undo",
                    "This program has not changed anything on this PC.",
                    IsProblem: false)
                : ChangeSummary.ForMany(results, "everything this program changed"));

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

    private async Task ApplyProfileAsync(ProfileSummary? profile)
    {
        if (profile is null || _backend is null)
            return;

        IsBusy = true;
        var activity = _activity.Begin(
            $"Applying preset '{profile.Name}' — {profile.TweakCount} tweak(s)…");
        try
        {
            var results = await _backend.ApplyProfileAsync(profile.Name, _shutdown.Token);
            Report(ChangeSummary.ForMany(results, $"the '{profile.Name}' preset"));

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

    private static bool IsSuccess(Outcome outcome)
        => outcome is not (Outcome.Failed or Outcome.RolledBack);

    private void SetStatus(string message, bool isError = false)
        => Report(new ChangeSummary(message, null, isError));

    /// <summary>Puts a summary into the activity panel: headline on top, meaning underneath.</summary>
    private void Report(ChangeSummary summary)
    {
        StatusMessage = summary.Headline;
        StatusDetail = summary.Detail;
        StatusIsError = summary.IsProblem;

        Raise(nameof(ActivityTitle));
        Raise(nameof(ActivityDetail));
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        // The live loop observes the same token, so cancelling is enough to end it. It is not
        // waited on: it can be parked inside a backend read, and blocking the shutdown path on
        // a pipe round-trip is how a window ends up refusing to close.
        _liveLoop = null;

        _backend?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _shutdown.Dispose();
    }
}
