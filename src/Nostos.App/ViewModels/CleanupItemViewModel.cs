using Nostos.App.Localization;
using Nostos.Core.Localization;
using Nostos.Win32.Cleanup;

namespace Nostos.App.ViewModels;

/// <summary>Where a row has got to.</summary>
public enum CleanupRowState
{
    Idle,
    Working,
    Cleaned,
    Failed,

    /// <summary>
    /// Pressed, and refused before anything was attempted.
    ///
    /// Its own state rather than a flavour of Failed: nothing failed, and nothing was touched.
    /// The row goes back to offering its button, because the thing that would change the answer
    /// is starting the program differently rather than trying again here.
    /// </summary>
    NeedsAdmin,
}

/// <summary>
/// One place that can be emptied.
///
/// The row's job is to make the trade legible before anybody presses anything: what this is, how
/// much it holds, and what it costs to lose. A cleaner that shows a size and a checkbox is asking
/// somebody to gamble with their own machine.
/// </summary>
public sealed class CleanupItemViewModel : ObservableObject
{
    private CleanupFinding _finding;
    private CleanupRowState _state;
    private string? _detail;

    public CleanupItemViewModel(CleanupFinding finding) => _finding = finding;

    public CleanupTarget Target => _finding.Target;

    public string Id => Target.Id;

    /// <summary>What this place is, in the reader's language.</summary>
    public string Name => Strings.GetOr($"clean.name.{Target.Id}", Target.Id);

    /// <summary>What is lost by emptying it. Every row has one; none of them is "nothing".</summary>
    public string Cost => Strings.GetOr($"clean.cost.{Target.Id}", "");

    public string Path => Target.Path;

    public string GroupName => Strings.GetOr(
        $"clean.group.{Target.Group.ToString().ToLowerInvariant()}", Target.Group.ToString());

    public long Bytes => _finding.Bytes;

    public string SizeText => AppItemViewModel.Format(_finding.Bytes);

    public bool HasAnything => _finding.HasAnything;

    /// <summary>True when pressing Clean would actually empty something.</summary>
    public bool CanClean => _finding.CanClean && _state is CleanupRowState.Idle or CleanupRowState.Failed;

    /// <summary>
    /// True when this one is out of reach until the program is started as administrator.
    ///
    /// Known from the measurement, so it is on the row before anybody presses anything.
    /// </summary>
    public bool NeedsAdmin => _finding.NeedsAdmin;

    /// <summary>
    /// Whether the row offers its button, which is a different question from whether the button
    /// would work.
    ///
    /// It used to be the same question, and that was the bug: a machine-wide folder measured at
    /// two gigabytes drew a size, a cost, a path and an empty space where every other row had a
    /// Clean button. Nothing on screen connected that gap to administrator rights, so the four
    /// rows that need them read as the feature being broken for them specifically. Now the
    /// button is there, and pressing it says what it needs.
    /// </summary>
    public bool ShowsCleanButton => _finding.HasAnything
        && _state is CleanupRowState.Idle or CleanupRowState.Failed or CleanupRowState.NeedsAdmin;

    public bool IsWorking => _state == CleanupRowState.Working;

    public double RowOpacity => _state == CleanupRowState.Cleaned ? 0.45 : 1.0;

    /// <summary>
    /// The count line: how many files, and how many were held back.
    ///
    /// The second number is why this exists. A scratch directory is mostly files written in the
    /// last day, and a size that looks far too small next to what Explorer reports needs an
    /// explanation standing beside it rather than a support thread.
    /// </summary>
    public string CountText
    {
        get
        {
            if (_finding.NeedsAdmin)
                return Strings.Get("clean.blocked");

            if (!_finding.Exists)
                return Strings.Get("clean.absent");

            if (_finding.Files == 0)
                return Strings.Get("clean.empty");

            return _finding.Held > 0
                ? Strings.Format("clean.files.held", _finding.Files, _finding.Held)
                : Strings.Format("clean.files", _finding.Files);
        }
    }

    public CleanupRowState State
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
    /// A list rather than a run of Raise calls, for the reason the Apps tab learned the hard
    /// way: PropertyChanged is marshalled to a UI thread that no test runs, so an omission here
    /// is invisible to the whole suite and shows up only as a row that quietly says nothing.
    /// </summary>
    public static readonly string[] StateDependent =
    [
        nameof(CanClean),
        nameof(ShowsCleanButton),
        nameof(IsWorking),
        nameof(RowOpacity),
        nameof(StatusText),
        nameof(HasStatus),
    ];

    public string? Detail
    {
        get => _detail;
        set
        {
            if (SetField(ref _detail, value))
            {
                Raise(nameof(StatusText));
                Raise(nameof(HasStatus));
            }
        }
    }

    public string StatusText => _state switch
    {
        CleanupRowState.Working => Strings.Get("clean.state.working"),
        CleanupRowState.Cleaned => _detail ?? Strings.Get("clean.state.cleaned"),
        CleanupRowState.Failed => _detail ?? Strings.Get("clean.state.failed"),
        CleanupRowState.NeedsAdmin => AdminNeeded.Explain,
        _ => "",
    };

    public bool HasStatus => _state != CleanupRowState.Idle;

    /// <summary>Replaces the measurement after a rescan, keeping the row object.</summary>
    public void Update(CleanupFinding finding)
    {
        _finding = finding;
        Raise(null);
    }

    /// <summary>Says what a finished sweep actually did, in the units the reader cares about.</summary>
    public void Describe(CleanupResult result)
    {
        // Asked before the problem list, because it is not one: nothing was attempted, so there
        // is nothing to report except what would make it possible.
        if (result.NeedsAdmin)
        {
            State = CleanupRowState.NeedsAdmin;
            Detail = null;
            return;
        }

        if (result.Problems.Count > 0)
        {
            State = CleanupRowState.Failed;
            Detail = result.Problems[0];
            return;
        }

        State = CleanupRowState.Cleaned;
        Detail = result.Skipped > 0
            ? Strings.Format("clean.done.skipped",
                AppItemViewModel.Format(result.BytesFreed), result.FilesRemoved, result.Skipped)
            : Strings.Format("clean.done",
                AppItemViewModel.Format(result.BytesFreed), result.FilesRemoved);
    }

    public void RefreshText() => Raise(null);
}
