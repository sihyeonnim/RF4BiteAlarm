using RF4Overlay.Core.Features;
using RF4Overlay.Features;

namespace RF4Overlay.Tests;

public sealed class FeatureCommandTests
{
    [Fact]
    public async Task DuplicateStartStopAndConcurrentTogglesAreSerialized()
    {
        var feature = new TestFeature();
        await using var runtime = new FeatureCommandDispatcher([feature]);
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await feature.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start))));
        Assert.Equal(1, feature.Starts);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Toggle))));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        Assert.Equal(0, feature.Active);
        Assert.Equal(1, feature.Peak);
        Assert.Equal(FeatureState.Stopped, runtime.GetStatuses()[0].State);
    }

    [Fact]
    public async Task AnnouncesOnlySuccessfulStateChanges()
    {
        var feature = new TestFeature();
        var announcements = new FakeAnnouncement();
        await using var runtime = new FeatureCommandDispatcher([feature], announcements);

        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await feature.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));

        Assert.Equal([(FeatureId.Metronome, true), (FeatureId.Metronome, false)], announcements.Items);
    }

    private sealed class FakeAnnouncement : IFeatureAnnouncement
    {
        public List<(FeatureId Feature, bool Running)> Items { get; } = [];
        public void Announce(FeatureId feature, bool running) => Items.Add((feature, running));
    }

    [Fact]
    public async Task ShutdownRejectsNewCommandsAndWaitsForCleanup()
    {
        var feature = new TestFeature();
        var runtime = new FeatureCommandDispatcher([feature]);
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await feature.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(runtime.DisposeAsync().AsTask(), runtime.DisposeAsync().AsTask());
        Assert.Equal(0, feature.Active);
        Assert.False((await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start))).Succeeded);
    }

    [Fact]
    public async Task FailureIsReportedWithoutTakingDownOtherFeature()
    {
        var healthy = new TestFeature();
        await using var runtime = new FeatureCommandDispatcher([healthy, new FailingFeature()]);
        var failed = new TaskCompletionSource<FeatureStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StatusChanged += (_, status) => { if (status.State == FeatureState.Faulted) failed.TrySetResult(status); };
        runtime.StatusChanged += (_, _) => throw new InvalidOperationException("Broken observer");
        await runtime.ExecuteAsync(new(FeatureId.BiteAlarm, FeatureAction.Start));
        Assert.Equal("Audio failed", (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.True((await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start))).Succeeded);
        await healthy.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledCommandDoesNotStart()
    {
        var feature = new TestFeature();
        await using var runtime = new FeatureCommandDispatcher([feature]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.False((await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start), cts.Token)).Succeeded);
        Assert.Equal(0, feature.Starts);
    }

    [Fact]
    public async Task UnknownAndUnavailableCommandsFail()
    {
        await using var empty = new FeatureCommandDispatcher([]);
        Assert.False((await empty.ExecuteAsync(new(FeatureId.BiteAlarm, FeatureAction.Start))).Succeeded);
        await using var planned = new FeatureCommandDispatcher(FeatureCatalog.Create(new MetronomeTests.FakeAudio(), new()));
        var statuses = planned.GetStatuses();
        Assert.Equal(Enum.GetValues<FeatureId>().Order(), statuses.Select(status => status.Id).Order());
        Assert.Equal(FeatureState.Stopped, Assert.Single(statuses, status => status.Id == FeatureId.Metronome).State);
        foreach (var status in statuses.Where(status => status.Id != FeatureId.Metronome))
        {
            Assert.Equal(FeatureState.Unavailable, status.State);
            Assert.False((await planned.ExecuteAsync(new(status.Id, FeatureAction.Start))).Succeeded);
        }
        Assert.Throws<ArgumentException>(() => new FeatureCommandDispatcher([new TestFeature(), new TestFeature()]));
    }

    private sealed class FailingFeature : IFeature
    {
        public FeatureStatus InitialStatus => new(FeatureId.BiteAlarm, "Failure", FeatureState.Stopped, "");
        public Task RunAsync(CancellationToken token) => throw new InvalidOperationException("Audio failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionRacingStopCannotLeaveStoppingOrLoseFailure(bool fail)
    {
        var feature = new CompletingFeature(fail);
        await using var runtime = new FeatureCommandDispatcher([feature]);
        using var releaseObserver = new ManualResetEventSlim();
        var terminalPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        runtime.StatusChanged += (_, status) =>
        {
            if (status.State is FeatureState.Stopped or FeatureState.Faulted && Interlocked.Increment(ref first) == 1)
            {
                terminalPublished.TrySetResult();
                releaseObserver.Wait(TimeSpan.FromSeconds(5));
            }
        };
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        feature.Finish.TrySetResult();
        await terminalPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        releaseObserver.Set();
        await stop;
        Assert.Equal(fail ? FeatureState.Faulted : FeatureState.Stopped, runtime.GetStatuses()[0].State);
        if (fail) Assert.Equal("Run failed", runtime.GetStatuses()[0].Error);
    }

    private sealed class CompletingFeature(bool fail) : IFeature
    {
        public FeatureStatus InitialStatus => new(FeatureId.Metronome, "Test", FeatureState.Stopped, "");
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(CancellationToken token)
        {
            await Finish.Task;
            if (fail) throw new IOException("Run failed");
        }
    }

    [Fact]
    public async Task CancellationCallbackFailureStillWaitsForFeatureCleanup()
    {
        var feature = new CallbackFailureFeature();
        await using var runtime = new FeatureCommandDispatcher([feature]);
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await feature.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False((await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop))).Succeeded);
        Assert.True(feature.Cleaned);
        Assert.Equal(FeatureState.Faulted, runtime.GetStatuses()[0].State);
    }

    [Fact]
    public async Task CancelQueuedStartWhileStopWaitsForCleanup()
    {
        var feature = new SlowCleanupFeature();
        await using var runtime = new FeatureCommandDispatcher([feature]);
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await feature.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        await feature.Cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancel = new CancellationTokenSource();
        var queued = runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start), cancel.Token);
        await cancel.CancelAsync();
        Assert.False((await queued.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.False(stop.IsCompleted);
        feature.Release.TrySetResult();
        Assert.True((await stop).Succeeded);
        Assert.Equal(FeatureState.Stopped, runtime.GetStatuses()[0].State);
    }

    private sealed class CallbackFailureFeature : IFeature
    {
        public FeatureStatus InitialStatus => new(FeatureId.Metronome, "Test", FeatureState.Stopped, "");
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cleaned;
        public async Task RunAsync(CancellationToken token)
        {
            using var registration = token.Register(() => throw new InvalidOperationException("Cancellation callback failed"));
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Cleaned = true; }
        }
    }

    private sealed class SlowCleanupFeature : IFeature
    {
        public FeatureStatus InitialStatus => new(FeatureId.Metronome, "Test", FeatureState.Stopped, "");
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Cleaning.TrySetResult(); await Release.Task; }
        }
    }

    private sealed class TestFeature : IFeature
    {
        public FeatureStatus InitialStatus => new(FeatureId.Metronome, "Test", FeatureState.Stopped, "");
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts, Active, Peak;
        public async Task RunAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Starts);
            var active = Interlocked.Increment(ref Active);
            Peak = Math.Max(Peak, active);
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}
