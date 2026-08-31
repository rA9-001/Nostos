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
    public static readonly BoolBrushConverter OnOff = new("#3DDC97", "#66738C");

    /// <summary>
    /// The wash behind <see cref="OnOff"/>, for the chip the label sits in.
    ///
    /// Translucent rather than mixed: the chip sits on a row that is transparent, tinted on
    /// hover and tinted again when selected, and an opaque fill would be the one thing on the
    /// row that did not respond to any of that.
    /// </summary>
    public static readonly BoolBrushConverter OnOffTint = new("#243DDC97", "#14FFFFFF");

    /// <summary>Red for an error in the status bar, normal text otherwise.</summary>
    public static readonly BoolBrushConverter ErrorNormal = new("#FF6B81", "#E9EDF7");
}

/// <summary>Colours the setup checklist by step outcome.</summary>
public sealed class StepStatusBrushConverter : IValueConverter
{
    public static readonly StepStatusBrushConverter Instance = new();

    private static readonly IBrush Done = SolidColorBrush.Parse("#3DDC97");
    private static readonly IBrush Skipped = SolidColorBrush.Parse("#93A0B8");
    private static readonly IBrush Failed = SolidColorBrush.Parse("#FFC24B");
    private static readonly IBrush Pending = SolidColorBrush.Parse("#66738C");

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
