using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Nostos.Core.Localization;

// Aliased rather than imported: this file also deals in file system paths through the rest of
// the namespace, and `Path` meaning two things in one file is how the wrong one gets used.
using Shape = Avalonia.Controls.Shapes.Path;

namespace Nostos.App.Views;

/// <summary>
/// The window shell, plus the title bar the app draws for itself.
///
/// Everything below is window chrome, which is why it is code behind rather than commands on a
/// view model: moving, maximising and closing a window are facts about this window object, not
/// state the rest of the program has any business knowing about. There is nothing to test here
/// that is not the platform's own behaviour.
/// </summary>
public partial class MainWindow : Window
{
    private Button? _maximiseButton;
    private Shape? _maximiseGlyph;

    /// <summary>
    /// False until the XAML has finished loading.
    ///
    /// This window sets WindowState in its own markup, and an attribute set during loading
    /// raises a property change while the name scope is still being built. Looking a control up
    /// by name at that moment throws "Could not find parent name scope" and takes the process
    /// down before a window ever appears. So the state handler does nothing until the tree it
    /// wants to reach into exists, and the constructor does the one update that was missed.
    /// </summary>
    private bool _loaded;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _maximiseButton = this.FindControl<Button>("MaximiseButton");
        _maximiseGlyph = this.FindControl<Shape>("MaximiseGlyph");
        _loaded = true;

        // The one tooltip in the window that is not set in markup, so it is the one piece of
        // text a language change cannot reach on its own.
        Strings.LanguageChanged += UpdateMaximiseButton;

        UpdateMaximiseButton();
    }

    protected override void OnClosed(EventArgs e)
    {
        Strings.LanguageChanged -= UpdateMaximiseButton;
        base.OnClosed(e);
    }

    /// <summary>
    /// Drag the window by its title bar.
    ///
    /// Safe to hang off the whole bar: every control in it that is meant to be clicked marks
    /// the pointer press handled before this sees it, so pressing a caption button does not
    /// also start a drag.
    /// </summary>
    private void TitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    /// <summary>Double click on the bar maximises or restores, as every other window does.</summary>
    private void TitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximised();

    private void MinimiseClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximiseClicked(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximised()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Covers the ways the state can change that are not the button: the keyboard, Aero
        // snap, dragging the window to the top of the screen, and another program arranging it.
        if (change.Property == WindowStateProperty)
            UpdateMaximiseButton();
    }

    /// <summary>
    /// Points the middle button at whichever of the two glyphs is currently true.
    ///
    /// A button that always shows the same square is the single most common flaw in a
    /// hand-rolled title bar: it leaves the reader unable to tell, from the window alone,
    /// whether clicking it will fill the screen or come back from it.
    /// </summary>
    private void UpdateMaximiseButton()
    {
        if (!_loaded)
            return;

        var maximised = WindowState == WindowState.Maximized;

        if (_maximiseButton is not null)
            ToolTip.SetTip(_maximiseButton, Strings.Get(maximised ? "window.restore" : "window.maximise"));

        if (_maximiseGlyph is null || Application.Current is not { } app)
            return;

        if (app.Resources.TryGetResource(
                maximised ? "RestoreIcon" : "MaximiseIcon",
                app.ActualThemeVariant,
                out var resource)
            && resource is Geometry geometry)
        {
            _maximiseGlyph.Data = geometry;
        }
    }
}
