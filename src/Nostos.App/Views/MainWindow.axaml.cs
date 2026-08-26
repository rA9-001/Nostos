using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nostos.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
