using System.Collections.ObjectModel;
using Nostos.Core.Abstractions;
using Nostos.Ipc;

namespace Nostos.App.ViewModels;

/// <summary>One row in the catalog list.</summary>
public sealed class TweakItemViewModel : ObservableObject
{
    private TweakStatusSummary _status;
    private bool _isBusy;
    private string? _groupHeader;
    private string? _groupDescription;

    public TweakItemViewModel(TweakStatusSummary status)
    {
        _status = status;

        foreach (var choice in status.Tweak.Choices)
        {
            var viewModel = new TweakChoiceViewModel(choice, currentSelection: null);
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

    public string Id => _status.Tweak.Id;
    public string Title => _status.Tweak.Title;
    public string Summary => _status.Tweak.Summary;
    public string Category => _status.Tweak.Category;

    /// <summary>The category's player-facing label -- "Stutter &amp; 1% Lows", not "stutter".</summary>
    public string CategoryName => TweakCategories.NameOf(Category);
    public Risk Risk => _status.Tweak.Risk;
    public Evidence Evidence => _status.Tweak.Evidence;
    public TweakScope Scope => _status.Tweak.Scope;
    public bool RequiresReboot => _status.Tweak.RequiresReboot;

    public bool IsApplied => _status.IsApplied;
    public bool IsManaged => _status.IsManagedByUs;
    public bool IsApplicable => _status.IsApplicable;
    public string StateDescription => _status.StateDescription;
    public string? NotApplicableReason => _status.NotApplicableReason;

    public string RiskText => Risk.ToString().ToLowerInvariant();
    public string EvidenceText => Evidence.ToString().ToLowerInvariant();
    public string ScopeText => Scope.ToString().ToLowerInvariant();

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
                parts.Add("session only");
            if (RequiresReboot)
                parts.Add("needs reboot");
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
    public string StateLabel => ShowsAsApplied ? "ON" : "OFF";

    /// <summary>True only when the tweak is both applicable here and actually applied.</summary>
    public bool ShowsAsApplied => IsApplicable && IsApplied;

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
