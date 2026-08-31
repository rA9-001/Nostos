using Nostos.Core.Localization;
using Nostos.App.Localization;
using System.Collections.ObjectModel;
using Nostos.Core.Abstractions;
using Nostos.Ipc;

namespace Nostos.App.ViewModels;

/// <summary>One row in the catalog list.</summary>
public sealed class TweakItemViewModel : ObservableObject
{
    private TweakStatusSummary _status;
    private bool _isBusy;
    private string? _superHeader;
    private string? _superDescription;
    private string? _groupHeader;
    private string? _groupDescription;

    public TweakItemViewModel(TweakStatusSummary status)
    {
        _status = status;

        foreach (var choice in status.Tweak.Choices)
        {
            var viewModel = new TweakChoiceViewModel(status.Tweak.Id, choice, currentSelection: null);
            viewModel.SelectionChanged += () => SelectionChanged?.Invoke(this);
            Choices.Add(viewModel);
        }
    }

    /// <summary>
    /// Settings the user can pick between, empty for most tweaks.
    ///
    /// Built once and kept across status refreshes: rebuilding them would throw away the
    /// user's selection every time the catalog reloads.
    /// </summary>
    public ObservableCollection<TweakChoiceViewModel> Choices { get; } = [];

    public bool HasChoices => Choices.Count > 0;

    /// <summary>Raised when a choice changes, so the owner can re-read this tweak's state.</summary>
    public event Action<TweakItemViewModel>? SelectionChanged;

    /// <summary>The current selections, in the form the engine and the wire expect.</summary>
    public IReadOnlyDictionary<string, string> SelectedOptions
        => Choices.ToDictionary(c => c.Id, c => c.Selected.Id, StringComparer.OrdinalIgnoreCase);

    public TweakStatusSummary Status
    {
        get => _status;
        set
        {
            _status = value;
            // Everything on this row is derived from the status record, so one refresh
            // invalidates the lot. Choices are deliberately not rebuilt here -- they hold the
            // user's selection, which a background refresh has no business resetting.
            Raise(null);
        }
    }

    /// <summary>
    /// Re-reads every string on this row.
    ///
    /// Used when the language changes. Everything visible here is a computed property over the
    /// status record, so there is nothing to recompute: the row only has to be told to ask
    /// again. Choices are left alone, as they are on a status refresh, because they hold the
    /// user's selection.
    /// </summary>
    public void RefreshText() => Raise(null);

    public string Id => _status.Tweak.Id;
    /// <summary>
    /// The title, in the user's language.
    ///
    /// Translated here rather than in the engine, because the language is a preference of the
    /// person looking at this window and the engine is shared: on an installed copy the catalog
    /// arrives over a pipe from a machine-wide service that has no user and no opinion about
    /// what language to speak. The English comes across the wire and is swapped for German at
    /// the last possible moment.
    /// </summary>
    public string Title => CatalogText.TweakTitle(Id, _status.Tweak.Title);

    public string Summary => CatalogText.TweakSummary(Id, _status.Tweak.Summary);
    public string Category => _status.Tweak.Category;

    /// <summary>The category's player-facing label -- "Stutter &amp; 1% Lows", not "stutter".</summary>
    public string CategoryName => CatalogText.CategoryName(Category);
    public Risk Risk => _status.Tweak.Risk;
    public Evidence Evidence => _status.Tweak.Evidence;
    public TweakScope Scope => _status.Tweak.Scope;

    /// <summary>
    /// True when this tweak has to be told which program it is about.
    ///
    /// Not the same question as the scope, though it was decided from it until
    /// process.persistent-priority arrived: that one writes HKLM and outlives every
    /// process it affects, so it is machine-scoped, and still needs to be pointed at an
    /// executable.
    /// </summary>
    public bool NeedsTarget => _status.Tweak.NeedsTarget;
    public bool RequiresReboot => _status.Tweak.RequiresReboot;

    public bool IsApplied => _status.IsApplied;
    public bool IsManaged => _status.IsManagedByUs;
    public bool IsApplicable => _status.IsApplicable;
    /// <summary>
    /// The raw read, e.g. <c>HwSchMode = 2 [Mode: Aggressive]</c>.
    ///
    /// Not translated, in either language. It is registry value names and the numbers behind
    /// them, shown in a monospace font, and it exists so that somebody comparing this window
    /// against regedit or a forum post sees the same characters in all three places.
    /// </summary>
    public string StateDescription => _status.StateDescription;

    /// <summary>
    /// Why this tweak cannot run here, in the reader's language when somebody has written one.
    ///
    /// The English arrives with the status and is the fallback; the key arrives beside it. See
    /// <see cref="Applicability"/> for why the choice is made here rather than where the text
    /// is produced -- in an installed copy that is a service running as SYSTEM, which has no
    /// user and therefore no language.
    /// </summary>
    public string? NotApplicableReason => _status.NotApplicableReasonKey is { } key
        ? Strings.Translate(key, _status.NotApplicableReason ?? key, _status.NotApplicableReasonArgs)
        : _status.NotApplicableReason;

    public string RiskText => CatalogText.Risk(Risk);
    public string EvidenceText => CatalogText.Evidence(Evidence);
    public string ScopeText => CatalogText.Scope(Scope);

    public string RiskBrushKey => Risk switch
    {
        Risk.Safe => "RiskSafe",
        Risk.Moderate => "RiskModerate",
        _ => "RiskHigh",
    };

    public string EvidenceBrushKey => Evidence switch
    {
        Evidence.Measured => "EvidenceMeasured",
        _ => "EvidencePlausible",
    };

    /// <summary>
    /// The outer band, above the inner one, or null when there is no outer band here.
    ///
    /// There are two levels because the unfiltered list has two things to say at once and
    /// saying only one of them made the other look broken. It is ordered Gaming-then-Windows
    /// and, inside each, category by category and safest first; with a single "Gaming" heading
    /// over all thirty-four of those rows, the risk column ran safe-to-moderate four separate
    /// times with nothing on screen marking where one category ended and the next began.
    ///
    /// So the outer band is the half of the catalog and the inner one is the category, which
    /// is the same shape the sidebar already draws. Inside a single category there is no outer
    /// band: the sidebar has already said which one you are in.
    /// </summary>
    public string? SuperHeader
    {
        get => _superHeader;
        set
        {
            if (SetField(ref _superHeader, value))
                Raise(nameof(HasSuperHeader));
        }
    }

    public bool HasSuperHeader => !string.IsNullOrEmpty(_superHeader);

    /// <summary>The line under the outer band. Set alongside the header.</summary>
    public string? SuperDescription
    {
        get => _superDescription;
        set => SetField(ref _superDescription, value);
    }

    /// <summary>
    /// Set on the first row of each section, so the list can print a band above it.
    ///
    /// Assigned by the owner after filtering rather than derived here, because whether a row is
    /// the first of its section depends on what else survived the filter, which the row itself
    /// cannot know.
    /// </summary>
    public string? GroupHeader
    {
        get => _groupHeader;
        set
        {
            if (SetField(ref _groupHeader, value))
                Raise(nameof(HasGroupHeader));
        }
    }

    public bool HasGroupHeader => !string.IsNullOrEmpty(_groupHeader);

    /// <summary>The one-line explanation printed under the band. Set alongside the header.</summary>
    public string? GroupDescription
    {
        get => _groupDescription;
        set => SetField(ref _groupDescription, value);
    }

    /// <summary>Shown as the row's badge line: "Performance · machine · needs reboot".</summary>
    public string Facets
    {
        get
        {
            var parts = new List<string> { CategoryName, ScopeText };
            if (_status.Tweak.Lifetime == TweakLifetime.SessionOnly)
                parts.Add(Strings.GetOr("facet.sessiononly", "session only"));
            if (RequiresReboot)
                parts.Add(Strings.GetOr("facet.reboot", "needs reboot"));
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>
    /// What the ON/OFF badge says.
    ///
    /// A tweak that cannot be applied here always reads OFF, whatever its own Read said.
    /// Applicability and state are answered by two independent methods, so a tweak can be both
    /// inapplicable and report itself as applied -- a service that exists but starts at Boot
    /// scope is the live example, and a machine-scope tweak on a Windows build that never had
    /// the setting is another. "ON" next to "not available on this PC" is a contradiction the
    /// reader has to resolve, and they resolve it by distrusting the badge.
    /// </summary>
    public string StateLabel => CatalogText.State(ShowsAsApplied);

    /// <summary>True only when the tweak is both applicable here and actually applied.</summary>
    public bool ShowsAsApplied => IsApplicable && IsApplied;

    /// <summary>True when this tweak belongs on the Windows Update tab.</summary>
    public bool IsWindowsUpdate => _status.Tweak.HasTag("windows-update");

    /// <summary>Opacity for the whole row, so an off tweak reads as off at a glance.</summary>
    public double RowOpacity => ShowsAsApplied ? 1.0 : 0.62;

    /// <summary>Crossfaded halves of the switch, the same two as a Startup row.</summary>
    public double OnOpacity => ShowsAsApplied ? 1 : 0;

    public double OffOpacity => ShowsAsApplied ? 0 : 1;

    /// <summary>Tooltip on an update row: what clicking it would do, in one sentence.</summary>
    public string ToggleHint => Strings.Format(
        ShowsAsApplied ? "updates.hint.off" : "updates.hint.on", Title);

    /// <summary>True while an apply or revert is in flight for this row.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
                Raise(nameof(IsInteractive));
        }
    }

    public bool IsInteractive => !_isBusy && IsApplicable;

    /// <summary>Docs page for this tweak, relative to the repository root.</summary>
    public string DocsPath => $"docs/tweaks/{Id}.md";

    public bool Matches(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return Id.Contains(search, StringComparison.OrdinalIgnoreCase)
               || Title.Contains(search, StringComparison.OrdinalIgnoreCase)
               || Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
               // Categories carry their own synonyms, so "hitching" and "framerate" find rows
               // whose titles are written in Windows vocabulary and never use those words.
               || (TweakCategories.Find(Category)?.Matches(search)
                   ?? Category.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}
