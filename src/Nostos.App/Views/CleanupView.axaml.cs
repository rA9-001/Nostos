using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nostos.App.ViewModels;

namespace Nostos.App.Views;

/// <summary>
/// The one piece of code-behind in this view, and it is here to keep the measurement lazy.
///
/// Measuring walks several directory trees and adds up file sizes -- seventeen thousand files
/// under Temp alone on the machine this was written on. Doing that during startup would slow the
/// window down for everybody, including the people who never open this tab; doing it never
/// leaves the tab showing an empty list and a button, which reads as broken.
///
/// <para>A tab's content is created the first time it is shown, so attaching to the visual tree
/// is exactly the moment somebody has asked to see this. Index-free on purpose: binding to the
/// tab control's selected index would break silently the next time a tab is inserted before
/// this one.</para>
/// </summary>
public partial class CleanupView : UserControl
{
    public CleanupView()
    {
        AvaloniaXamlLoader.Load(this);

        AttachedToVisualTree += (_, _) =>
        {
            // The row count is the latch rather than a flag of its own: a measurement that could
            // not run left none, and coming back to the tab should then try again rather than
            // remember a failure forever.
            if (DataContext is MainWindowViewModel { Cleanup.Count: 0 } window)
                _ = window.RequestCleanupAsync();
        };
    }
}
