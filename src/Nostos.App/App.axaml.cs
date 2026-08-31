using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nostos.App.ViewModels;
using Nostos.Core.Localization;
using Nostos.Core.Settings;
using Nostos.App.Views;

namespace Nostos.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before the window is built, so it is drawn in the right language once rather
            // than drawn in English and corrected.
            Strings.Language = AppSettings.Load().InterfaceLanguage;

            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Kick the initial load after the window exists so the UI can show a spinner
            // instead of a blank frame while the catalog is read.
            desktop.MainWindow.Opened += async (_, _) => await viewModel.InitialiseAsync();
            desktop.ShutdownRequested += (_, _) => viewModel.Dispose();

            // Raised once, by the settings panel, after the user has removed Nostos from the
            // machine. There is nothing left for the window to show at that point, and leaving
            // it up would invite clicks on a catalog that can no longer do anything.
            viewModel.ExitRequested += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
