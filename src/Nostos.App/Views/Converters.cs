using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;

namespace Nostos.App.Views;

/// <summary>
/// Resolves a resource key held in a view model into the brush it names.
///
/// Lets the view models describe a tweak's risk and evidence as semantic keys
/// ("RiskModerate") instead of importing Avalonia types to hand back colours.
/// </summary>
public sealed class ResourceBrushConverter : IValueConverter
{
    public static readonly ResourceBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is null)
            return Brushes.Gray;

        return Application.Current.Resources.TryGetResource(
                   key, Application.Current.ActualThemeVariant, out var resource)
               && resource is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Turns a category id into the label a player reads: "stutter" becomes "Stutter &amp; 1% Lows".
///
/// The list is bound to the ids rather than to objects on purpose. The ids are what the
/// selection, the filter, the profiles and the CLI all agree on, and swapping the collection to
/// display objects would mean the sidebar and everything else disagreed about what a category is.
/// </summary>
public sealed class CategoryNameConverter : IValueConverter
{
    public static readonly CategoryNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string id
            ? id switch
            {
                MainWindowViewModel.AllCategories => "All tweaks",
                MainWindowViewModel.NotApplicableCategory => "Not applicable",
                _ => TweakCategories.NameOf(id),
            }
            : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The group band that belongs above a category in the sidebar, or "" for the rest.
///
/// The sidebar is a flat ListBox of category ids and needs to stay one: its selection is bound
/// two ways, and a list whose items are a mix of headings and selectable rows either lets you
/// select a heading or needs a second control to coordinate with. Putting the heading inside
/// the first item of each group keeps one list and one selection.
///
/// Which category leads a group is fixed by <see cref="TweakCategories.All"/>, so this needs no
/// knowledge of what the list currently holds.
/// </summary>
public sealed class CategoryGroupHeaderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string pseudo && pseudo == MainWindowViewModel.NotApplicableCategory)
            return "THIS PC";

        if (value is not string id || TweakCategories.Find(id) is not { } category)
            return "";

        var leads = TweakCategories.InGroup(category.Group).FirstOrDefault();
        return leads?.Id == category.Id
            ? TweakCategories.NameOfGroup(category.Group).ToUpperInvariant()
            : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True when <see cref="CategoryGroupHeaderConverter"/> would produce a heading.</summary>
public sealed class CategoryHasGroupHeaderConverter : IValueConverter
{
    private static readonly CategoryGroupHeaderConverter Header = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Header.Convert(value, typeof(string), parameter, culture) is string s && s.Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a bool to an opacity, so something can be hidden without leaving the layout.
///
/// <c>IsVisible</c> would be the obvious choice and is the wrong one here: collapsing an element
/// changes the size of its parent, so a spinner that appears for 40ms shoves everything below it
/// down and back. Fading in place costs a fixed slot and never moves anything.
/// </summary>
public sealed class BoolOpacityConverter : IValueConverter
{
    public static readonly BoolOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>The same, inverted, for the pair of glyphs that share the activity panel's slot.</summary>
public sealed class InverseBoolOpacityConverter : IValueConverter
{
    public static readonly InverseBoolOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.0 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
