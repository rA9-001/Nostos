using Nostos.App.ViewModels;

namespace Nostos.App.Tests;

/// <summary>
/// When a progress indicator is worth showing.
///
/// Driven through an injected delay rather than the clock, so these assert the state machine
/// exactly instead of racing it. Each test controls when each delay completes.
/// </summary>
public sealed class ActivityTrackerTests
{
    /// <summary>Hands out a completion source per delay, so the test decides when time passes.</summary>
    private sealed class ManualClock
    {
        private readonly Queue<TaskCompletionSource> _pending = new();

        public Task Delay(TimeSpan _, CancellationToken __)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(source);
            return source.Task;
        }

        /// <summary>Lets the next scheduled delay elapse, if one is waiting.</summary>
        public async Task<bool> AdvanceAsync()
        {
            if (_pending.Count == 0)
                return false;

            _pending.Dequeue().SetResult();
            await Task.Yield();
            await Task.Delay(1);
            return true;
        }

        /// <summary>Lets every delay elapse, including ones scheduled by the continuations.</summary>
        public async Task DrainAsync()
        {
            while (await AdvanceAsync())
            {
            }
        }
    }

    /// <summary>
    /// Ends a scope, releasing the minimum-visible delay it may be holding.
    ///
    /// Needed because disposing a visible scope parks on the clock: awaiting it directly, with
    /// nothing advancing the clock, deadlocks the test rather than the product.
    /// </summary>
    private static async Task EndAsync(ActivityTracker.Scope scope, ManualClock clock)
    {
        var ending = scope.DisposeAsync();
        await clock.DrainAsync();
        await ending;
    }

    private static (ActivityTracker Tracker, ManualClock Clock) Build()
    {
        var clock = new ManualClock();
        return (new ActivityTracker(clock.Delay), clock);
    }

    [Fact]
    public async Task Work_that_finishes_quickly_never_shows_anything()
    {
        // The whole point. Most operations complete in tens of milliseconds now, and a spinner
        // that appears and vanishes inside two frames reads as a glitch, not as progress.
        var (tracker, clock) = Build();

        var scope = tracker.Begin("Applying something");
        Assert.False(tracker.IsVisible);

        await EndAsync(scope, clock);

        Assert.False(tracker.IsVisible);
        Assert.Null(tracker.Caption);

        // Even once the show-delay elapses, there is nothing left to show.
        await clock.AdvanceAsync();
        Assert.False(tracker.IsVisible);
    }

    [Fact]
    public async Task Work_that_outlasts_the_delay_shows_the_indicator()
    {
        var (tracker, clock) = Build();

        var scope = tracker.Begin("Applying something");
        await clock.AdvanceAsync();

        Assert.True(tracker.IsVisible);
        Assert.Equal("Applying something", tracker.Caption);

        await EndAsync(scope, clock);
    }

    [Fact]
    public async Task Once_shown_it_stays_up_long_enough_to_be_read()
    {
        // Without this the flicker just moves to work that takes slightly longer than the
        // show-delay: it would appear and then vanish a frame later.
        var (tracker, clock) = Build();

        var scope = tracker.Begin("Applying something");
        await clock.AdvanceAsync();
        Assert.True(tracker.IsVisible);

        var ending = scope.DisposeAsync();

        // Still up: the minimum-visible delay has not elapsed.
        Assert.True(tracker.IsVisible);

        await clock.AdvanceAsync();
        await ending;

        Assert.False(tracker.IsVisible);
    }

    [Fact]
    public async Task A_caption_can_change_without_the_indicator_blinking()
    {
        // An apply becomes a re-read partway through. That is one continuous activity, not two.
        var (tracker, clock) = Build();

        var scope = tracker.Begin("Applying something");
        await clock.AdvanceAsync();

        scope.Describe("Checking what actually changed");

        Assert.True(tracker.IsVisible);
        Assert.Equal("Checking what actually changed", tracker.Caption);

        await EndAsync(scope, clock);
    }

    [Fact]
    public async Task Back_to_back_operations_do_not_blink_between_them()
    {
        // A second operation starting while the first is being held open keeps the indicator up
        // rather than letting it drop for a frame and come back.
        var (tracker, clock) = Build();

        var first = tracker.Begin("First");
        await clock.AdvanceAsync();
        Assert.True(tracker.IsVisible);

        var ending = first.DisposeAsync();
        var second = tracker.Begin("Second");

        await clock.AdvanceAsync();
        await ending;

        Assert.True(tracker.IsVisible);
        Assert.Equal("Second", tracker.Caption);

        await EndAsync(second, clock);
    }

    [Fact]
    public void Work_is_reported_as_running_before_it_is_reported_as_visible()
    {
        // Callers that must not overlap the user (the background refresh loop) ask IsRunning.
        // Callers that draw ask IsVisible. Conflating them would either flicker the UI or let
        // a background read race an apply.
        var (tracker, _) = Build();

        _ = tracker.Begin("Applying something");

        Assert.True(tracker.IsRunning);
        Assert.False(tracker.IsVisible);
    }

    [Fact]
    public async Task A_nested_scope_does_not_end_the_outer_one()
    {
        var (tracker, clock) = Build();

        var outer = tracker.Begin("Outer");
        await clock.AdvanceAsync();

        var inner = tracker.Begin("Inner");
        await EndAsync(inner, clock);

        Assert.True(tracker.IsVisible);
        Assert.True(tracker.IsRunning);

        await EndAsync(outer, clock);

        Assert.False(tracker.IsRunning);
    }
}
