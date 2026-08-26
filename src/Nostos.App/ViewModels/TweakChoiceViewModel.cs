using System.Collections.ObjectModel;
using Nostos.Core.Abstractions;

namespace Nostos.App.ViewModels;

/// <summary>
/// One selectable setting on a tweak, bound to a radio-button group in the detail pane.
///
/// Radio buttons rather than a dropdown, deliberately: a dropdown hides every option but the
/// selected one, and the explanations are the point. If they are collapsed behind a click, the
/// user is picking from a list of names again.
/// </summary>
public sealed class TweakChoiceViewModel : ObservableObject
{
    private TweakChoiceOptionViewModel _selected;

    public TweakChoiceViewModel(TweakChoice choice, string? currentSelection)
    {
        Id = choice.Id;
        Title = choice.Title;
        Description = choice.Description;

        foreach (var option in choice.Options)
            Options.Add(new TweakChoiceOptionViewModel(this, option));

        var wanted = currentSelection ?? choice.DefaultOption;
        _selected = Options.FirstOrDefault(o =>
                        string.Equals(o.Id, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? Options[0];

        _selected.SetChecked(true);
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }

    /// <summary>Matches the small uppercase section labels used elsewhere in the detail pane.</summary>
    public string TitleUpper => Title.ToUpperInvariant();

    public ObservableCollection<TweakChoiceOptionViewModel> Options { get; } = [];

    /// <summary>Raised when the user picks a different option, so the row's state can be re-read.</summary>
    public event Action? SelectionChanged;

    public TweakChoiceOptionViewModel Selected
    {
        get => _selected;
        private set
        {
            if (ReferenceEquals(_selected, value))
                return;

            var previous = _selected;
            _selected = value;
            previous.SetChecked(false);

            Raise(nameof(Selected));
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Unique per instance so two choices on the same tweak do not share a radio group.</summary>
    public string GroupName { get; } = $"choice-{Guid.NewGuid():n}";

    internal void Select(TweakChoiceOptionViewModel option) => Selected = option;
}

public sealed class TweakChoiceOptionViewModel : ObservableObject
{
    private readonly TweakChoiceViewModel _owner;
    private bool _isChecked;

    public TweakChoiceOptionViewModel(TweakChoiceViewModel owner, TweakChoiceOption option)
    {
        _owner = owner;
        Id = option.Id;
        Title = option.Title;
        Description = option.Description;
        Recommended = option.Recommended;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public bool Recommended { get; }

    public string GroupName => _owner.GroupName;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetField(ref _isChecked, value))
                return;

            // Only the button being turned ON drives the selection. Avalonia also raises this
            // for the one being turned off, and treating that as a change would fight itself.
            if (value)
                _owner.Select(this);
        }
    }

    internal void SetChecked(bool value) => SetField(ref _isChecked, value, nameof(IsChecked));
}
