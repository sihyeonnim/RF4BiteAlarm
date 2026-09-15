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
        await using var planned = new FeatureCommandDispatcher(FeatureCatalog.Create());
        Assert.All(planned.GetStatuses(), status => Assert.Equal(FeatureState.Unavailable, status.State));
        Assert.False((await planned.ExecuteAsync(new(FeatureId.BiteAlarm, FeatureAction.Start))).Succeeded);
        Assert.Throws<ArgumentException>(() => new FeatureCommandDispatcher([new TestFeature(), new TestFeature()]));
    }

    private sealed class FailingFeature : IFeature
    {
        public FeatureStatus InitialStatus => new(FeatureId.BiteAlarm, "Failure", FeatureState.Stopped, "");
        public Task RunAsync(CancellationToken token) => throw new InvalidOperationException("Audio failed");
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
