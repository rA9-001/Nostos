using System.Collections.ObjectModel;
using Nostos.App.Startup;
using Nostos.Core.Localization;
using Nostos.Core.Settings;
using Nostos.Core.Updates;

namespace Nostos.App.ViewModels;

/// <summary>
/// What the settings panel needs from the window behind it.
///
/// Narrow on purpose. The panel can ask about updates, undo everything, and tell the window to
/// stop talking to the machine — and that is the whole surface. Anything wider and the removal
/// path would start to be a second, differently-behaved copy of the ordinary one.
/// </summary>
public interface ISettingsHost
{
    /// <summary>Changes this program has made and not yet undone.</summary>
    int OutstandingCount { get; }

    /// <summary>Runs an update check now and returns what to show the person who asked.</summary>
    Task<string> CheckForUpdatesAsync(CancellationToken ct = default);

    /// <summary>Undoes every change in the journal. Returns how many were undone.</summary>
    Task<int> RevertEverythingAsync(CancellationToken ct = default);

    /// <summary>
    /// Drops the backend and stops the background refresh.
    ///
    /// Called immediately before the service is stopped and the data folder is deleted, so that
    /// nothing is holding the journal open and no timer is about to re-create the folder that
    /// was just removed.
    /// </summary>
    Task DetachAsync();
}

/// <summary>One row in the "how often" list.</summary>
/// <param name="Value">The stored enum.</param>
/// <param name="Label">What the user reads, in the language they picked.</param>
public sealed record CadenceOption(UpdateCadence Value, string Label);

/// <summary>
/// One row in the language list.
/// </summary>
/// <param name="Value">The stored enum.</param>
/// <param name="Label">
/// The language's name written in itself. A picker that offers "German" to somebody who cannot
/// read English is a picker that cannot be used by the person who needs it.
/// </param>
public sealed record LanguageOption(Language Value, string Label);

/// <summary>
/// The settings panel: what the app does about updates, and how to get rid of it.
///
/// The two live together because they are the only two decisions in this program that are about
/// the program rather than about the machine, and because they are the two a person goes looking
/// for in the same place.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly ISystemRemover _remover;
    private readonly ISettingsStore _store;

    private AppSettings _settings;
    private string? _installDirectory;

    /// <summary>Set once removal has run, so nothing here re-creates what was just deleted.</summary>
    private bool _removed;

    public SettingsViewModel(ISettingsHost host, ISystemRemover? remover = null, ISettingsStore? store = null)
    {
        _host = host;
        _remover = remover ?? new WindowsRemover();
        _store = store ?? new FileSettingsStore();
        _settings = _store.Load();

        Languages =
        [
            new LanguageOption(Language.English, Strings.NativeNameOf(Language.English)),
            new LanguageOption(Language.German, Strings.NativeNameOf(Language.German)),
        ];

        Cadences = BuildCadences();

        // The labels in the list above are text like everything else, so they have to be
        // rebuilt when the language changes. The list is replaced rather than mutated because
        // SelectedCadence finds its row by value, so a new set of rows re-selects correctly.
        Strings.LanguageChanged += OnLanguageChanged;

        CheckNowCommand = new AsyncCommand(CheckNowAsync, () => !IsCheckingUpdates && !_removed);
        ArmRemovalCommand = new AsyncCommand(ArmRemovalAsync, () => !IsRemoving && !RemovalFinished);
        CancelRemovalCommand = new AsyncCommand(() => { RemovalArmed = false; return Task.CompletedTask; });
        RemoveCommand = new AsyncCommand(RemoveAsync, () => !IsRemoving && !RemovalFinished);
        CloseCommand = new AsyncCommand(() => { Exit?.Invoke(); return Task.CompletedTask; });
    }

    /// <summary>Raised when the user asks to close the app after removing it.</summary>
    public event Action? Exit;

    // ----------------------------------------------------------------- language

    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>
    /// The interface language, applied the moment it is picked.
    ///
    /// Applying it here rather than on the next launch is the difference between a setting and
    /// a promise. Everything on screen is bound through the string table, so the window
    /// rewrites itself; nothing is reloaded and nothing is lost.
    /// </summary>
    public LanguageOption SelectedLanguage
    {
        get => Languages.First(l => l.Value == _settings.InterfaceLanguage);
        set
        {
            // A ComboBox reports a null selection while its items are being rebuilt.
            if (value is null || value.Value == _settings.InterfaceLanguage)
                return;

            Persist(_settings with { Language = value.Value });
            Strings.Language = value.Value;
            Raise();
        }
    }

    private static IReadOnlyList<CadenceOption> BuildCadences() =>
    [
        new CadenceOption(UpdateCadence.EveryLaunch, Strings.Get("cadence.everylaunch")),
        new CadenceOption(UpdateCadence.Daily, Strings.Get("cadence.daily")),
        new CadenceOption(UpdateCadence.Weekly, Strings.Get("cadence.weekly")),
    ];

    private void OnLanguageChanged()
    {
        Cadences = BuildCadences();
        Raise(nameof(Cadences));
        Raise(nameof(SelectedCadence));
        Raise(nameof(UpdatePolicyText));
        Raise(nameof(LastCheckedText));
        Raise(nameof(RevertChangesLabel));
    }

    /// <summary>The settings as they stand, for the window's own launch-time check.</summary>
    public AppSettings Current => _settings;

    // ------------------------------------------------------------------ updates

    public IReadOnlyList<CadenceOption> Cadences { get; private set; }

    public AsyncCommand CheckNowCommand { get; }

    public bool CheckForUpdates
    {
        get => _settings.UpdateChecksEnabled;
        set
        {
            if (_settings.UpdateChecksEnabled == value)
                return;

            Persist(_settings with { CheckForUpdates = value });
            Raise();
            Raise(nameof(CadenceEnabled));
            Raise(nameof(UpdatePolicyText));
        }
    }

    public CadenceOption SelectedCadence
    {
        get => Cadences.First(c => c.Value == _settings.Cadence);
        set
        {
            // A ComboBox reports a null selection while its items are being rebuilt.
            if (value is null || value.Value == _settings.Cadence)
                return;

            Persist(_settings with { Cadence = value.Value });
            Raise();
            Raise(nameof(UpdatePolicyText));
        }
    }

    /// <summary>The cadence only means anything while checking is on.</summary>
    public bool CadenceEnabled => _settings.UpdateChecksEnabled;

    /// <summary>The whole policy in one sentence, so it can be read without decoding two controls.</summary>
    public string UpdatePolicyText => !_settings.UpdateChecksEnabled
        ? Strings.Get("settings.policy.off")
        : _settings.Cadence switch
        {
            UpdateCadence.Daily => Strings.Get("settings.policy.daily"),
            UpdateCadence.Weekly => Strings.Get("settings.policy.weekly"),
            _ => Strings.Get("settings.policy.everylaunch"),
        };

    public string VersionText
    {
        get
        {
            var version = UpdateClient.CurrentVersion();

            // A local build is stamped 0.0.0 so that it can never be mistaken for a release --
            // which also means it thinks every release is newer, and saying so here is kinder
            // than letting the banner look like a bug.
            return version == new Version(0, 0, 0, 0)
                ? Strings.Get("settings.version.dev")
                : Strings.Format("settings.version", version.ToString(3));
        }
    }

    public string LastCheckedText
    {
        get
        {
            if (_settings.LastCheckedUtc is not { } last)
                return Strings.Get("settings.checked.never");

            var age = DateTimeOffset.UtcNow - last;
            return age.TotalMinutes switch
            {
                < 2 => Strings.Get("settings.checked.now"),
                < 60 => Strings.Format("settings.checked.minutes", (int)age.TotalMinutes),
                < 24 * 60 => Strings.Format("settings.checked.hours", (int)age.TotalHours),
                _ => Strings.Format("settings.checked.date", Strings.DateText(last.ToLocalTime(), withYear: true)),
            };
        }
    }

    public bool IsCheckingUpdates
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                CheckNowCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>What the last manual check found, or null. Unlike the launch check, this is news.</summary>
    public string? CheckResult
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                Raise(nameof(HasCheckResult));
        }
    }

    public bool HasCheckResult => !string.IsNullOrEmpty(CheckResult);

    /// <summary>Set when the preferences file could not be written. Almost always a permissions problem.</summary>
    public string? SaveProblem
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                Raise(nameof(HasSaveProblem));
        }
    }

    public bool HasSaveProblem => !string.IsNullOrEmpty(SaveProblem);

    private void Persist(AppSettings updated)
    {
        _settings = updated;

        // After removal there is no data folder, and saving would quietly re-create it along
        // with the file inside. "Nothing left on the PC" has to survive the user clicking
        // something in a panel that is still on screen.
        if (_removed)
            return;

        SaveProblem = _store.Save(updated)
            ? null
            : Strings.Format("settings.saveproblem", AppSettings.Path);
    }

    /// <summary>Records that a check happened, whatever its outcome. Called by the window too.</summary>
    public void RecordCheck(DateTimeOffset? at = null)
    {
        Persist(_settings with { LastCheckedUtc = at ?? DateTimeOffset.UtcNow });
        Raise(nameof(LastCheckedText));
    }

    private async Task CheckNowAsync()
    {
        IsCheckingUpdates = true;
        CheckResult = null;
        try
        {
            CheckResult = await _host.CheckForUpdatesAsync().ConfigureAwait(true);
            RecordCheck();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            CheckResult = e.Message;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    // ------------------------------------------------------------------ removal

    public AsyncCommand ArmRemovalCommand { get; }
    public AsyncCommand CancelRemovalCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand CloseCommand { get; }

    /// <summary>What removal will do on this machine, in the order it will do it.</summary>
    public ObservableCollection<string> RemovalPlan { get; } = [];

    /// <summary>What it actually did.</summary>
    public ObservableCollection<string> RemovalSteps { get; } = [];

    /// <summary>Anything that would not go, outside the app's own folder. Normally empty.</summary>
    public ObservableCollection<string> Leftovers { get; } = [];

    /// <summary>What the user deletes themselves when everything else is gone.</summary>
    public ObservableCollection<string> DeleteByHand { get; } = [];

    public bool HasLeftovers => Leftovers.Count > 0;

    /// <summary>True once the confirm step is showing. The first click arms; the second acts.</summary>
    public bool RemovalArmed
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                RaiseRemovalVisibility();
        }
    }

    /// <summary>The three states of the removal card are exclusive, and each is one property.</summary>
    public bool ShowRemoveButton => !RemovalArmed && !IsRemoving && !RemovalFinished;

    public bool ShowRemovalConfirmation => RemovalArmed && !IsRemoving && !RemovalFinished;

    public bool ShowRemovalOutcome => RemovalFinished;

    /// <summary>True while there is a status line worth a place on screen.</summary>
    public bool HasRemovalStatus => IsRemoving || RemovalFinished;

    private void RaiseRemovalVisibility()
    {
        Raise(nameof(ShowRemoveButton));
        Raise(nameof(ShowRemovalConfirmation));
        Raise(nameof(ShowRemovalOutcome));
        Raise(nameof(HasRemovalStatus));
    }

    public bool IsRemoving
    {
        get;
        private set
        {
            if (!SetField(ref field, value))
                return;

            RemoveCommand.RaiseCanExecuteChanged();
            ArmRemovalCommand.RaiseCanExecuteChanged();
            RaiseRemovalVisibility();
        }
    }

    /// <summary>True when removal has run to the end, whether or not everything went.</summary>
    public bool RemovalFinished
    {
        get;
        private set
        {
            if (SetField(ref field, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
                ArmRemovalCommand.RaiseCanExecuteChanged();
                RaiseRemovalVisibility();
            }
        }
    }

    /// <summary>Whether to undo every applied tweak first. On by default, and the whole point.</summary>
    public bool RevertChanges
    {
        get;
        set
        {
            if (SetField(ref field, value))
                Raise(nameof(RemovalWarning));
        }
    } = true;

    public bool HasChangesToRevert => _host.OutstandingCount > 0;

    public string RevertChangesLabel => _host.OutstandingCount == 1
        ? Strings.Get("removal.revert.one")
        : Strings.Format("removal.revert.many", _host.OutstandingCount);

    /// <summary>The one sentence that has to be read before the second click.</summary>
    public string RemovalWarning => RevertChanges
        ? Strings.Get("removal.warning.reverting")
        : Strings.Get("removal.warning.keeping");

    public string? RemovalStatus
    {
        get;
        private set => SetField(ref field, value);
    }

    /// <summary>Why removal stopped short, or null. A declined prompt lands here.</summary>
    public string? RemovalProblem
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                Raise(nameof(HasRemovalProblem));
        }
    }

    public bool HasRemovalProblem => !string.IsNullOrEmpty(RemovalProblem);

    /// <summary>The folder this copy runs from. Shown in the plan before anything happens.</summary>
    public string? InstallDirectory => _installDirectory;

    /// <summary>
    /// Reads the machine and builds the plan.
    ///
    /// Deferred until the panel is opened rather than done in the constructor: it queries the
    /// Service Control Manager, and every window that never opens Settings should never pay for
    /// that, nor for the surprise of a view model that touches the SCM to exist.
    /// </summary>
    public void PrepareRemoval()
    {
        if (_removed)
            return;

        var targets = _remover.Inspect();
        _installDirectory = targets.InstallDirectory;

        DeleteByHand.Clear();
        foreach (var path in targets.DeleteByHand)
            DeleteByHand.Add(path);

        RemovalPlan.Clear();

        if (HasChangesToRevert)
            RemovalPlan.Add(RevertChangesLabel + Strings.Get("removal.warning.unlessuntick"));

        if (targets.ServiceInstalled)
            RemovalPlan.Add(Strings.Get("removal.plan.service"));

        RemovalPlan.Add(Strings.Format("removal.plan.dataroot", targets.DataRoot));

        if (targets.LocalState is { } cache)
            RemovalPlan.Add(Strings.Format("removal.plan.cache", cache));

        // Two paths get named by file, not in full. A one-file copy's executable and its data
        // folder sit in the same directory, and printing both absolute paths in a bullet meant
        // the same forty characters twice on one line.
        RemovalPlan.Add(targets.DeleteByHand.Count == 1
            ? Strings.Format("removal.plan.byhand.one", targets.DeleteByHand[0])
            : Strings.Format(
                "removal.plan.byhand.many",
                string.Join(Strings.Get("removal.plan.and"),
                            targets.DeleteByHand.Select(Path.GetFileName)),
                targets.InstallDirectory));

        Raise(nameof(HasChangesToRevert));
        Raise(nameof(RevertChangesLabel));
        Raise(nameof(InstallDirectory));
    }

    private Task ArmRemovalAsync()
    {
        PrepareRemoval();
        RemovalArmed = true;
        return Task.CompletedTask;
    }

    private async Task RemoveAsync()
    {
        RemovalArmed = false;
        IsRemoving = true;
        RemovalProblem = null;
        RemovalSteps.Clear();
        Leftovers.Clear();

        try
        {
            if (RevertChanges && _host.OutstandingCount > 0)
            {
                RemovalStatus = Strings.Get("removal.status.reverting");
                var reverted = await _host.RevertEverythingAsync().ConfigureAwait(true);
                RemovalSteps.Add(reverted == 1
                    ? Strings.Get("removal.status.undone.one")
                    : Strings.Format("removal.status.undone.many", reverted));
            }

            // Before the service is stopped and the folder is deleted, not after: the pipe and
            // the refresh loop both hold state that is about to stop existing.
            RemovalStatus = Strings.Get("removal.status.removing");
            await _host.DetachAsync().ConfigureAwait(true);

            var result = await _remover.RemoveAsync().ConfigureAwait(true);

            foreach (var step in result.Done)
                RemovalSteps.Add(step);

            foreach (var leftover in result.Leftovers)
                Leftovers.Add(leftover);

            // Read off the disk by the remover rather than predicted here, so what the panel
            // tells the user to delete is what is actually still there.
            DeleteByHand.Clear();
            foreach (var path in result.DeleteByHand)
                DeleteByHand.Add(path);

            _removed = true;
            RemovalProblem = result.Problem;
            RemovalFinished = true;

            RemovalStatus = result.Problem is not null
                ? Strings.Get("removal.status.failed")
                : result.Completed
                    ? Strings.Get("removal.status.done")
                    : Strings.Get("removal.status.partial");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            RemovalProblem = e.Message;
            RemovalStatus = Strings.Get("removal.status.failed");
        }
        finally
        {
            IsRemoving = false;
            Raise(nameof(HasLeftovers));
            CheckNowCommand.RaiseCanExecuteChanged();
        }
    }
}
