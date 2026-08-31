using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nostos.App.Views;

public partial class StartupView : UserControl
{
    public StartupView() => AvaloniaXamlLoader.Load(this);
}
