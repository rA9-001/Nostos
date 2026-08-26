namespace Nostos.App.ViewModels;

/// <summary>
/// Decides when a "working" indicator is worth showing, and for how long.
///
/// Naively binding a spinner to "is an operation running" looks broken for fast work. Most
/// operations here finish in tens of milliseconds now that the engine runs off the UI thread,
/// so the indicator appeared and vanished within a frame or two: a flicker that reads as a
/// glitch rather than as progress.
///
/// Two thresholds fix it, and they have to work together:
///
/// <b>Show after a delay.</b> Work that finishes inside <see cref="ShowAfter"/> never shows an
/// indicator at all. From the user's side it simply happened, which is the truth.
///
/// <b>Once shown, stay shown.</b> Work that crosses that line holds the indicator for at least
/// <see cref="MinimumVisible"/>, even if it completes immediately afterwards. Without this the
/// threshold just moves the flicker to operations that take slightly longer than the delay.
///
/// The delays are injected rather than called directly so tests can drive the state machine
/// without sleeping and without depending on timing.
/// </summary>
public sealed class ActivityTracker : ObservableObject
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _depth;
    private string? _caption;
    private bool _isVisible;

    public ActivityTracker(Func<TimeSpan, CancellationToken, Task>? delay = null)
        => _delay = delay ?? ((duration, ct) => Task.Delay(duration, ct));

    /// <summary>Work faster than this never shows an indicator.</summary>
    public TimeSpan ShowAfter { get; init; } = TimeSpan.FromMilliseconds(180);

    /// <summary>Once shown, the indicator stays up at least this long.</summary>
    public TimeSpan MinimumVisible { get; init; } = TimeSpan.FromMilliseconds(650);

    /// <summary>True when the indicator should be drawn. Never true for very fast work.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetField(ref _isVisible, value);
    }

    /// <summary>
    /// What is happening, in words. Set as soon as work begins even when
    /// <see cref="IsVisible"/> is still false, so nothing has to be re-derived at the moment
    /// the indicator appears.
    /// </summary>
    public string? Caption
    {
        get => _caption;
        set => SetField(ref _caption, value);
    }

    /// <summary>True from the instant work starts, regardless of what is drawn.</summary>
    public bool IsRunning => _depth > 0;

    /// <summary>
    /// Marks the start of a unit of work. Dispose it when the work is done.
    ///
    /// Nested scopes are counted, so a refresh that happens inside an apply does not put the
    /// indicator away while the apply is still going.
    /// </summary>
    public Scope Begin(string caption)
    {
        _depth++;
        Caption = caption;
        Raise(nameof(IsRunning));

        if (_depth == 1)
            _ = ShowIfSlowAsync();

        return new Scope(this);
    }

    private async Task ShowIfSlowAsync()
    {
        await _delay(ShowAfter, CancellationToken.None).ConfigureAwait(true);

        // Finished inside the grace period: the user never needed to know it was working.
        if (_depth == 0)
            return;

        IsVisible = true;
        _shownAt = DateTimeOffset.UtcNow;
    }

    private DateTimeOffset _shownAt;

    private async Task EndAsync()
    {
        if (--_depth > 0)
            return;

        Raise(nameof(IsRunning));

        if (!IsVisible)
        {
            // Never became visible, so there is nothing to hold or to clear on screen.
            Caption = null;
            return;
        }

        var shownFor = DateTimeOffset.UtcNow - _shownAt;
        if (shownFor < MinimumVisible)
            await _delay(MinimumVisible - shownFor, CancellationToken.None).ConfigureAwait(true);

        // Another operation may have started while we were holding. Its scope owns the
        // indicator now, and putting it away here would flicker the very thing this prevents.
        if (_depth > 0)
            return;

        IsVisible = false;
        Caption = null;
    }

    /// <summary>One unit of work. Disposing it ends the activity, honouring the minimum.</summary>
    public readonly struct Scope : IAsyncDisposable
    {
        private readonly ActivityTracker _owner;

        internal Scope(ActivityTracker owner) => _owner = owner;

        /// <summary>Changes the caption mid-flight, e.g. from "Applying" to "Checking".</summary>
        public void Describe(string caption) => _owner.Caption = caption;

        public ValueTask DisposeAsync() => new(_owner.EndAsync());
    }
}
