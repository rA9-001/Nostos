using Nostos.App.ViewModels;

namespace Nostos.App.Startup;

public enum StepStatus
{
    Pending,
    Running,

    /// <summary>Already fine; nothing was done.</summary>
    Ok,

    /// <summary>Was missing or wrong, and this run repaired it.</summary>
    Fixed,

    /// <summary>Not applicable, or the user has previously declined it.</summary>
    Skipped,

    /// <summary>Could not be repaired. The app still starts; the reason is shown.</summary>
    Failed,
}

/// <summary>One line in the setup checklist, observable so the window can show it live.</summary>
public sealed class StartupStep : ObservableObject
{
    private StepStatus _status = StepStatus.Pending;
    private string? _detail;

    public StartupStep(string title) => Title = title;

    public string Title { get; }

    public StepStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                Raise(nameof(Glyph));
                Raise(nameof(IsRunning));
            }
        }
    }

    public string? Detail
    {
        get => _detail;
        set => SetField(ref _detail, value);
    }

    public bool IsRunning => _status == StepStatus.Running;

    public string Glyph => _status switch
    {
        StepStatus.Ok => "✓",
        StepStatus.Fixed => "✓",
        StepStatus.Skipped => "–",
        StepStatus.Failed => "!",
        _ => "·",
    };
}
