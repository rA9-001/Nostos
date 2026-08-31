using Nostos.App.Localization;
using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Core.Localization;
using Nostos.Ipc;

namespace Nostos.App.ViewModels;

/// <summary>Where one row of a profile has got to while the profile is being applied.</summary>
public enum RowRunState
{
    /// <summary>Nothing is happening. What the rows read before Apply and after a refresh.</summary>
    Idle,

    /// <summary>This is the tweak the program is working on right now.</summary>
    Running,

    Done,

    /// <summary>Deliberately not attempted: not applicable here, not elevated, already set.</summary>
    Skipped,

    Failed,
}

/// <summary>
/// One line of a profile's contents: a tweak it would apply.
///
/// A class rather than a record, and mutable, because a row has to be able to change while the
/// profile is being applied. The rows used to be rebuilt on every read of
/// <see cref="ProfileViewModel.Tweaks"/>, which is fine for a static list and useless for a live
/// one -- a progress report arriving for a row that has already been replaced would update an
/// object nothing is displaying.
/// </summary>
public sealed class ProfileTweakViewModel : ObservableObject
{
    private RowRunState _runState;

    public ProfileTweakViewModel(
        string id,
        string title,
        string category,
        string riskText,
        string riskBrushKey,
        bool isApplied,
        bool isMissing)
    {
        Id = id;
        Title = title;
        Category = category;
        RiskText = riskText;
        RiskBrushKey = riskBrushKey;
        IsApplied = isApplied;
        IsMissing = isMissing;
    }

    /// <summary>The tweak id, which is what a progress report names.</summary>
    public string Id { get; }

    /// <summary>The tweak's title, translated, or its id when the catalog has no such row.</summary>
    public string Title { get; }

    /// <summary>Where it is filed. The group heading rather than a word on the row.</summary>
    public string Category { get; }

    public string RiskText { get; }

    public string RiskBrushKey { get; }

    /// <summary>True when this machine already matches what the tweak would do.</summary>
    public bool IsApplied { get; }

    /// <summary>True when the catalog has no tweak with this id.</summary>
    public bool IsMissing { get; }

    public RowRunState RunState
    {
        get => _runState;
        set
        {
            if (SetField(ref _runState, value))
            {
                Raise(nameof(IsRunning));
                Raise(nameof(StateGlyph));
                Raise(nameof(StateBrushKey));
                Raise(nameof(RowOpacity));
                Raise(nameof(HasGlyph));
            }
        }
    }

    public bool IsRunning => _runState == RowRunState.Running;

    /// <summary>Hidden while the spinner is up: a stale tick beside a spinner invites belief.</summary>
    public bool HasGlyph => _runState != RowRunState.Running;

    /// <summary>
    /// A tick for what is set, an empty circle for what applying would change, and while a
    /// profile is being applied, what happened to this row.
    /// </summary>
    public string StateGlyph => _runState switch
    {
        RowRunState.Done => "✓",
        RowRunState.Skipped => "–",
        RowRunState.Failed => "✕",
        _ => IsApplied ? "✓" : "○",
    };

    public string StateBrushKey => _runState switch
    {
        RowRunState.Done => "RiskSafe",
        RowRunState.Skipped => "Muted",
        RowRunState.Failed => "DangerText",
        _ => IsApplied ? "RiskSafe" : "TextFaint",
    };

    /// <summary>
    /// Dims a row that would change nothing.
    ///
    /// The question somebody opens a profile card to answer is "what would this do to my
    /// machine", and on a machine where half the profile is already applied the honest answer is
    /// the other half. Dimming lets that half be read on its own without hiding anything.
    ///
    /// While a profile is being applied the rule inverts: the row being worked on is the one
    /// worth looking at, so it goes to full strength whatever its prior state.
    /// </summary>
    public double RowOpacity => _runState switch
    {
        RowRunState.Running => 1.0,
        RowRunState.Idle => IsApplied ? 0.5 : 1.0,
        _ => 0.75,
    };
}

/// <summary>
/// One category's worth of a profile's contents, as a heading and its rows.
///
/// The list used to be flat, with the category repeated as a word on every row -- forty-two
/// rows, each carrying a label that was the same as the one above it four times out of five.
/// That is a heading pretending to be a column. Grouping says the same thing once and turns an
/// undifferentiated list into something with shape.
/// </summary>
public sealed record ProfileCategoryGroup(
    string Name,
    IReadOnlyList<ProfileTweakViewModel> Tweaks)
{
    public string CountText => $"{Tweaks.Count(t => t.IsApplied)}/{Tweaks.Count}";
}

/// <summary>
/// One profile, and the list of what it would do.
///
/// A profile used to be a name, a sentence and a count. "Apply 42 tweaks" is a lot to ask
/// somebody to agree to on the strength of one sentence, and the honest answer to "what does
/// this actually change?" was to go and read a JSON file. The card opens now.
///
/// The rows are resolved against the catalog the window already has rather than sent with the
/// profile, because the catalog is where a tweak's title, category and risk live, and those are
/// translated. The profile only carries ids; ids are not language.
/// </summary>
public sealed class ProfileViewModel : ObservableObject
{
    private readonly Func<string, TweakItemViewModel?> _lookup;
    private bool _isExpanded;
    private bool _isApplying;
    private int _done;
    private int _total;

    private IReadOnlyList<ProfileTweakViewModel>? _tweaks;
    private IReadOnlyList<ProfileCategoryGroup>? _groups;

    public ProfileViewModel(ProfileSummary summary, Func<string, TweakItemViewModel?> lookup)
    {
        Summary = summary;
        _lookup = lookup;
    }

    /// <summary>The wire record, which is what applying it takes.</summary>
    public ProfileSummary Summary { get; }

    /// <summary>The identifier: the file name, and what `nos apply-profile` takes.</summary>
    public string Name => Summary.Name;

    /// <summary>
    /// How the name is written on the card.
    ///
    /// Only capitalisation, and only for the profiles this build ships. The identifier stays
    /// what it is -- somebody reading "Basic" here and typing `basic` at a prompt is not being
    /// misled, and the loader matches case-insensitively -- but a card headed "basic" next to
    /// two sentences of prose reads like a variable name that escaped.
    /// </summary>
    public string DisplayName => Strings.Translate($"profile.{Summary.Name}.name", Summary.Name);

    public string Description
        => Strings.Translate($"profile.{Summary.Name}.description", Summary.Description);

    public string CountText => Strings.Format("profiles.count", Summary.TweakCount);

    /// <summary>
    /// False when the profile arrived without its list -- from a service too old to send one.
    ///
    /// The card then behaves exactly as it did before there was a list: a name, a sentence, a
    /// count and an Apply button. Better than an arrow that opens onto nothing.
    /// </summary>
    public bool CanExpand => Summary.TweakIds is { Count: > 0 };

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
                Raise(nameof(ToggleGlyphKey));
        }
    }

    /// <summary>Points down when closed and up when open, so the arrow says what clicking does.</summary>
    public string ToggleGlyphKey => _isExpanded ? "ChevronUpIcon" : "ChevronDownIcon";

    public void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>
    /// What this profile would apply, in the order it lists it.
    ///
    /// Built once and kept, unlike the version this replaced, which rebuilt on every read. The
    /// rows have to be stable objects now: a progress report names a tweak id, and it has to
    /// find the row that is actually on screen rather than one that was discarded between the
    /// last binding and this one.
    /// </summary>
    public IReadOnlyList<ProfileTweakViewModel> Tweaks => _tweaks ??= Build();

    private IReadOnlyList<ProfileTweakViewModel> Build() =>
    [
        .. (Summary.TweakIds ?? []).Select(id =>
        {
            if (_lookup(id) is not { } tweak)
            {
                // A profile naming a tweak this build does not have. Shown rather than dropped:
                // the count on the card comes from the profile, so silently skipping the row
                // would leave the list one shorter than the number above it with nothing to
                // explain the difference.
                return new ProfileTweakViewModel(
                    id, id, "", Strings.Get("profiles.unknown"), "RiskHigh",
                    isApplied: false, isMissing: true);
            }

            return new ProfileTweakViewModel(
                id,
                tweak.Title,
                tweak.CategoryName,
                CatalogText.Risk(tweak.Risk),
                tweak.RiskBrushKey,
                // What the machine currently reports, not what the profile intends. A row the
                // reader has already applied by hand should not be sold to them a second time.
                isApplied: tweak.ShowsAsApplied,
                isMissing: false);
        }),
    ];

    /// <summary>
    /// The same rows, gathered under their category headings.
    ///
    /// Category order follows the sidebar rather than the profile's own order or the alphabet,
    /// so a profile card reads down in the same sequence as the catalog it is drawn from.
    /// </summary>
    public IReadOnlyList<ProfileCategoryGroup> Groups => _groups ??=
    [
        .. Tweaks
            .GroupBy(t => t.Category)
            .OrderBy(g => TweakCategories.OrderOf(
                TweakCategories.All.FirstOrDefault(c =>
                    string.Equals(c.Name, g.Key, StringComparison.OrdinalIgnoreCase))?.Id ?? g.Key))
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new ProfileCategoryGroup(
                g.Key.Length == 0 ? Strings.Get("profiles.unknown") : g.Key, [.. g])),
    ];

    /// <summary>How much of this profile the machine already matches, for the card header.</summary>
    public string AppliedText
    {
        get
        {
            var rows = Tweaks;
            return rows.Count == 0
                ? ""
                : Strings.Format("profiles.applied", rows.Count(t => t.IsApplied), rows.Count);
        }
    }

    public bool HasAppliedText => Summary.TweakIds is { Count: > 0 };

    // ------------------------------------------------------------------ applying

    /// <summary>True while this profile is being applied. Only ever one card at a time.</summary>
    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (SetField(ref _isApplying, value))
            {
                Raise(nameof(ProgressText));
                Raise(nameof(ProgressFraction));
                Raise(nameof(HasCountedProgress));
            }
        }
    }

    /// <summary>"12 of 42", or empty when the backend cannot say where it has got to.</summary>
    public string ProgressText => _total > 0
        ? Strings.Format("profiles.progress", _done, _total)
        : "";

    /// <summary>0 to 1, for the bar. Meaningless unless <see cref="HasCountedProgress"/>.</summary>
    public double ProgressFraction => _total > 0 ? (double)_done / _total : 0;

    /// <summary>
    /// True once a real progress report has arrived.
    ///
    /// The bar is indeterminate until then, and stays indeterminate for a backend that cannot
    /// report -- the service applies a profile in one call over a pipe that carries one response.
    /// An indeterminate bar says "working"; a determinate one that was never fed would say
    /// "0 of 42" for the whole run and then jump, which is worse than admitting to not knowing.
    /// </summary>
    public bool HasCountedProgress => _total > 0;

    /// <summary>
    /// Opens the card and clears every row back to Idle, ready to be worked through.
    ///
    /// Opening it is the point: a collapsed card would show a bar and nothing else, and the
    /// whole reason for this is being able to see which tweak the program is on.
    /// </summary>
    public void BeginRun()
    {
        _done = 0;
        _total = 0;

        foreach (var row in Tweaks)
            row.RunState = RowRunState.Idle;

        IsExpanded = CanExpand;
        IsApplying = true;
    }

    /// <summary>Moves the card on by one report. Safe to call for an id this profile lacks.</summary>
    public void Report(BatchProgress progress)
    {
        _total = progress.Total;

        var row = Tweaks.FirstOrDefault(t =>
            string.Equals(t.Id, progress.TweakId, StringComparison.OrdinalIgnoreCase));

        if (progress.Outcome is not { } outcome)
        {
            if (row is not null)
                row.RunState = RowRunState.Running;
        }
        else
        {
            _done = progress.Index;

            if (row is not null)
            {
                row.RunState = outcome switch
                {
                    Outcome.Failed or Outcome.RolledBack => RowRunState.Failed,
                    Outcome.Skipped or Outcome.NothingToRevert => RowRunState.Skipped,
                    _ => RowRunState.Done,
                };
            }
        }

        Raise(nameof(ProgressText));
        Raise(nameof(ProgressFraction));
        Raise(nameof(HasCountedProgress));
    }

    /// <summary>
    /// Ends the run and leaves the outcome glyphs where they are.
    ///
    /// Deliberately not cleared: the reader has just watched forty rows go past, and the state
    /// they end in is the answer to "did that work". They are reset by the refresh that follows,
    /// which rebuilds the rows from what the machine now actually reports -- which is the more
    /// trustworthy answer of the two, and worth waiting the extra moment for.
    /// </summary>
    public void EndRun() => IsApplying = false;

    /// <summary>
    /// Throws the rows away so they are rebuilt from current state on the next read.
    ///
    /// Called after a refresh, because <see cref="ProfileTweakViewModel.IsApplied"/> is a
    /// snapshot of what the machine said at the time the row was made.
    /// </summary>
    public void Invalidate()
    {
        _tweaks = null;
        _groups = null;
        Raise(nameof(Tweaks));
        Raise(nameof(Groups));
        Raise(nameof(AppliedText));
    }

    /// <summary>Re-reads everything the string table feeds. Called when the language changes.</summary>
    public void RefreshText()
    {
        Raise(nameof(DisplayName));
        Raise(nameof(Description));
        Raise(nameof(CountText));
        Raise(nameof(ProgressText));
        Invalidate();
    }
}
