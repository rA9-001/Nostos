using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Nostos.App.Views;

/// <summary>Two-colour converters for the handful of places a boolean drives a colour.</summary>
public sealed class BoolBrushConverter : IValueConverter
{
    private readonly IBrush _whenTrue;
    private readonly IBrush _whenFalse;

    public BoolBrushConverter(string whenTrue, string whenFalse)
    {
        _whenTrue = SolidColorBrush.Parse(whenTrue);
        _whenFalse = SolidColorBrush.Parse(whenFalse);
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? _whenTrue : _whenFalse;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class BoolBrushes
{
    /// <summary>Green when a tweak is currently applied, grey when it is not.</summary>
    public static readonly BoolBrushConverter OnOff = new("#4ade80", "#64748b");

    /// <summary>Red for an error in the status bar, normal text otherwise.</summary>
    public static readonly BoolBrushConverter ErrorNormal = new("#f87171", "#cbd5e1");
}

/// <summary>Colours the setup checklist by step outcome.</summary>
public sealed class StepStatusBrushConverter : IValueConverter
{
    public static readonly StepStatusBrushConverter Instance = new();

    private static readonly IBrush Done = SolidColorBrush.Parse("#4ade80");
    private static readonly IBrush Skipped = SolidColorBrush.Parse("#94a3b8");
    private static readonly IBrush Failed = SolidColorBrush.Parse("#fbbf24");
    private static readonly IBrush Pending = SolidColorBrush.Parse("#64748b");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Startup.StepStatus.Ok or Startup.StepStatus.Fixed => Done,
            Startup.StepStatus.Skipped => Skipped,
            // Amber rather than red: a failed step never stops the app, it only narrows it.
            Startup.StepStatus.Failed => Failed,
            _ => Pending,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class StepBrushes
{
    public static readonly StepStatusBrushConverter ForStatus = StepStatusBrushConverter.Instance;
}
