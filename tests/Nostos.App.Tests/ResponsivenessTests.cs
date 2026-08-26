using System.Collections.Concurrent;
using Nostos.App.Backends;
using Nostos.App.ViewModels;
using Nostos.Core.Abstractions;
using Nostos.Ipc;

namespace Nostos.App.Tests;

/// <summary>
/// The window stays usable while work is in flight, and says what it is doing.
///
/// The bug these exist for could not be seen from the code: every operation was correctly
/// awaited and correctly wrapped in a busy flag, but the in-process engine completes
/// synchronously, so the await never yielded. The flag was set and cleared inside a single UI
/// thread turn -- the spinner never painted, and the window was frozen for the duration.
/// </summary>
public sealed class ResponsivenessTests
{
    /// <summary>A backend whose operations only finish when the test says so.</summary>
    private sealed class GatedBackend : FakeBackend
    {
        public TaskCompletionSource Gate { get; } = new();

        /// <summary>Gates the catalog read too, but only once the test arms it.</summary>
        public TaskCompletionSource? ReadGate { get; set; }

        public override async Task<IReadOnlyList<TweakStatusSummary>> GetStatusAsync(
            CancellationToken ct = default)
        {
            if (ReadGate is not null)
                await ReadGate.Task;

            return await base.GetStatusAsync(ct);
        }

        public override async Task<IReadOnlyList<ChangeResult>> ApplyAsync(
            string tweakId,
            IReadOnlyDictionary<string, string>? options = null,
            bool dryRun = false,
            CancellationToken ct = default)
        {
            await Gate.Task;
            return await base.ApplyAsync(tweakId, options, dryRun, ct);
        }
    }

    private static GatedBackend Catalog()
    {
        var backend = new GatedBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.one", title: "First tweak"));
        backend.Statuses.Add(FakeBackend.Tweak("a.two", title: "Second tweak"));
        return backend;
    }

    /// <summary>
    /// A tracker with both thresholds collapsed to nothing, so tests about <em>what</em> is shown
    /// are not also tests about <em>when</em>. The when is covered by ActivityTrackerTests.
    /// </summary>
    private static ActivityTracker Instant() => new((_, _) => Task.CompletedTask);

    private static async Task<MainWindowViewModel> LoadedAsync(
        FakeBackend backend, ActivityTracker? activity = null)
    {
        var viewModel = new MainWindowViewModel(backend, activity ?? Instant());
        await viewModel.InitialiseAsync();
        return viewModel;
    }

    [Fact]
    public async Task A_row_reports_itself_busy_for_as_long_as_the_work_takes()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        var apply = viewModel.ApplyCommand.ExecuteAsync(row);

        // The point: this assertion runs while the operation is still outstanding. Before the
        // fix there was no moment at which it could have run.
        Assert.True(row.IsBusy);
        Assert.True(viewModel.IsWorking);

        backend.Gate.SetResult();
        await apply;

        Assert.False(row.IsBusy);
        Assert.False(viewModel.IsWorking);
    }

    [Fact]
    public async Task The_busy_caption_names_the_operation_and_the_tweak()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        var apply = viewModel.ApplyCommand.ExecuteAsync(row);

        Assert.NotNull(viewModel.BusyText);
        Assert.Contains("Applying", viewModel.BusyText);
        Assert.Contains("First tweak", viewModel.BusyText);

        backend.Gate.SetResult();
        await apply;

        // Cleared afterwards, so the spinner's caption never outlives the spinner.
        Assert.Null(viewModel.BusyText);
    }

    [Fact]
    public async Task Other_rows_stay_untouched_while_one_is_working()
    {
        // A single row's operation must not present as a whole-window freeze: the other rows
        // are still readable and the list is still there.
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        var apply = viewModel.ApplyCommand.ExecuteAsync(row);

        Assert.False(viewModel.Tweaks.Single(t => t.Id == "a.two").IsBusy);
        Assert.Equal(2, viewModel.Tweaks.Count);

        backend.Gate.SetResult();
        await apply;
    }

    [Fact]
    public async Task The_same_row_cannot_be_applied_twice_at_once()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        var first = viewModel.ApplyCommand.ExecuteAsync(row);
        var second = viewModel.ApplyCommand.ExecuteAsync(row);

        backend.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["a.one"], backend.Applied);
    }

    [Fact]
    public async Task A_failed_operation_still_clears_the_spinner()
    {
        // Otherwise the row spins for the rest of the session and looks hung.
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        var apply = viewModel.ApplyCommand.ExecuteAsync(row);
        backend.Gate.SetException(new InvalidOperationException("nope"));
        await apply;

        Assert.False(row.IsBusy);
        Assert.Null(viewModel.BusyText);
        Assert.False(viewModel.IsWorking);
        Assert.True(viewModel.StatusIsError);
    }

    [Fact]
    public async Task A_whole_window_operation_raises_the_flag_the_progress_banner_binds_to()
    {
        // The banner at the top of the window is bound to IsWorking, not to IsBusy, so this is
        // the property that decides whether progress is visible at all. Batch operations set
        // IsBusy; a single row sets its own flag. Both have to reach IsWorking.
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);

        backend.ReadGate = new TaskCompletionSource();
        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.IsWorking);
        Assert.Contains("current settings", viewModel.BusyText);

        backend.ReadGate.SetResult();
        await refresh;

        Assert.False(viewModel.IsWorking);
        Assert.Null(viewModel.BusyText);
    }

    [Fact]
    public async Task Startup_says_what_it_is_doing_too()
    {
        // Once the setup overlay closes, the first catalog read is still running. Without a
        // caption that is an unlabelled busy state on a window seen for the first time.
        var backend = Catalog();
        backend.ReadGate = new TaskCompletionSource();

        var viewModel = new MainWindowViewModel(backend, Instant());
        var start = viewModel.InitialiseAsync();

        Assert.True(viewModel.IsWorking);
        Assert.Contains("Reading your PC", viewModel.BusyText);

        backend.ReadGate.SetResult();
        await start;

        Assert.False(viewModel.IsWorking);
    }

    [Fact]
    public async Task A_change_that_finishes_instantly_never_flashes_the_progress_panel()
    {
        // Reported from use: fast work made the progress indicator appear and vanish, which
        // "looks unprofessional and weird". With the real thresholds, work this quick is simply
        // not announced -- the panel keeps showing the last result and then shows the new one.
        var backend = Catalog();
        backend.Gate.SetResult();

        var viewModel = await LoadedAsync(backend, new ActivityTracker());
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        await viewModel.ApplyCommand.ExecuteAsync(row);

        Assert.False(viewModel.IsWorking);
        Assert.Equal(["a.one"], backend.Applied);
    }

    [Fact]
    public async Task The_activity_panel_always_has_something_to_say()
    {
        // It occupies a fixed slot whether or not anything is happening, so an empty headline
        // would be a blank box rather than an absent one.
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.one"));

        var viewModel = await LoadedAsync(backend);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.ActivityTitle));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ActivityDetail));
    }

    [Fact]
    public async Task Filtering_keeps_the_rows_it_already_had()
    {
        // The background refresh re-filters every few seconds. If that emptied and refilled the
        // bound collection, the list would drop its scroll position and selection on every
        // tick, and the rows would flicker under the cursor.
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.one"));
        backend.Statuses.Add(FakeBackend.Tweak("a.two"));

        var viewModel = await LoadedAsync(backend);
        var before = viewModel.Tweaks.ToList();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(before.Count, viewModel.Tweaks.Count);
        for (var i = 0; i < before.Count; i++)
            Assert.Same(before[i], viewModel.Tweaks[i]);
    }

    [Fact]
    public async Task A_refresh_keeps_the_selection()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.one"));
        backend.Statuses.Add(FakeBackend.Tweak("a.two"));

        var viewModel = await LoadedAsync(backend);
        viewModel.SelectedTweak = viewModel.Tweaks[1];
        var selected = viewModel.SelectedTweak;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(selected, viewModel.SelectedTweak);
        Assert.True(viewModel.HasSelection);
    }

    [Fact]
    public async Task The_displayed_state_says_how_old_it_is()
    {
        var backend = new FakeBackend();
        backend.Statuses.Add(FakeBackend.Tweak("a.one"));

        var viewModel = new MainWindowViewModel(backend, Instant());
        Assert.Equal("not read yet", viewModel.LastUpdatedText);

        await viewModel.InitialiseAsync();

        // The claim is that nothing shown is outdated; this is the property that lets a user
        // check it rather than take our word for it.
        Assert.Equal("live", viewModel.LastUpdatedText);
    }

    [Fact]
    public async Task A_row_that_is_working_is_not_interactive()
    {
        var backend = Catalog();
        var viewModel = await LoadedAsync(backend);
        var row = viewModel.Tweaks.Single(t => t.Id == "a.one");

        Assert.True(row.IsInteractive);

        var apply = viewModel.ApplyCommand.ExecuteAsync(row);
        Assert.False(row.IsInteractive);

        backend.Gate.SetResult();
        await apply;

        Assert.True(row.IsInteractive);
    }
}

/// <summary>
/// The in-process backend does its work somewhere other than the caller's thread.
///
/// Asserted against a real single-threaded context rather than by comparing thread ids, because
/// a thread-id comparison passes by luck when the test itself happens to be on a pool thread.
/// This reproduces the actual situation: a thread that is the only one allowed to paint.
/// </summary>
public sealed class LocalBackendThreadingTests
{
    /// <summary>A stand-in for the UI thread: one thread, a queue, and nothing else.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = [];
        private readonly Thread _thread;

        public SingleThreadContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "fake-ui" };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => Post(d, state);

        /// <summary>Runs work on the single thread and waits for it, as a message loop would.</summary>
        public T Run<T>(Func<Task<T>> work)
        {
            var done = new TaskCompletionSource<T>();

            Post(async _ =>
            {
                SetSynchronizationContext(this);
                try
                {
                    done.SetResult(await work());
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            }, null);

            return done.Task.GetAwaiter().GetResult();
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (work, state) in _queue.GetConsumingEnumerable())
                work(state);
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    [Fact]
    public void The_continuation_comes_back_to_the_calling_thread()
    {
        // The other half of the contract, and the one that breaks bindings if it is wrong.
        // Work goes to the pool, but whatever runs *after* the await touches view models and
        // ObservableCollections, so it has to land back on the thread that owns them.
        using var ui = new SingleThreadContext();
        var backend = new LocalBackend();

        var resumedOn = ui.Run(async () =>
        {
            await backend.GetStatusAsync();
            return Environment.CurrentManagedThreadId;
        });

        Assert.Equal(ui.ThreadId, resumedOn);
    }

    [Fact]
    public void The_read_yields_instead_of_completing_inline()
    {
        // The precise defect: every tweak's Read is synchronous inside an already-completed
        // Task, so awaiting it used to run the whole catalog read inline on the caller. If this
        // ever regresses, the returned task is completed before anything has had a chance to
        // schedule it.
        using var ui = new SingleThreadContext();
        var backend = new LocalBackend();

        var completedInline = ui.Run(() =>
        {
            var task = backend.GetStatusAsync();
            return Task.FromResult(task.IsCompleted);
        });

        Assert.False(completedInline,
            "LocalBackend.GetStatusAsync completed synchronously, which means it ran the whole "
            + "catalog read on the caller's thread. In the app that thread is the one that paints.");
    }
}
