using RF4Overlay.Core.Input;
using RF4Overlay.Features.AutoPilking;

namespace RF4Overlay.Tests;

public sealed class AutoPilkingTests
{
    [Fact]
    public void SettingsDefaultToRightMouseAndRequestedCycle()
    {
        var settings = new AutoPilkingSettings();
        var cycle = settings.Snapshot();

        Assert.Equal(AutomationInput.MouseRight, cycle.Input);
        Assert.Equal(TimeSpan.FromSeconds(1), cycle.HoldDuration);
        Assert.Equal(TimeSpan.FromSeconds(3.5), cycle.ReleaseDuration);
    }

    [Fact]
    public void SettingsRejectModifiersAndOutOfRangeDurations()
    {
        var settings = new AutoPilkingSettings();
        Assert.Throws<ArgumentException>(() => settings.Input = AutomationInput.Keyboard(0x11));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.HoldSeconds = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ReleaseSeconds = 60.1);
    }

    [Fact]
    public async Task FeatureUsesConfiguredSingleInputAndStopsThroughCancellation()
    {
        var automation = new FakeAutomation();
        var settings = new AutoPilkingSettings
        {
            Input = AutomationInput.Keyboard((byte)'P'),
            HoldSeconds = 0.4,
            ReleaseSeconds = 0.1
        };
        var feature = new AutoPilkingFeature(automation, settings, new FakeForegroundGate(true));
        using var cancellation = new CancellationTokenSource();

        var run = feature.RunAsync(cancellation.Token);
        var call = await automation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutomationInput.Keyboard((byte)'P'), call.Input);
        Assert.InRange(call.Duration, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.9));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(automation.CancellationObserved);
    }

    [Fact]
    public async Task FeaturePausesOutsideRf4AndResumesWhenRf4Returns()
    {
        var automation = new CountingAutomation();
        var foreground = new FakeForegroundGate(false);
        var feature = new AutoPilkingFeature(automation, new() { HoldSeconds = 10, ReleaseSeconds = 10 }, foreground);
        using var cancellation = new CancellationTokenSource();

        var run = feature.RunAsync(cancellation.Token);
        await Task.Delay(100);
        Assert.Equal(0, automation.StartCount);

        foreground.IsForeground = true;
        await automation.WaitForStartsAsync(1).WaitAsync(TimeSpan.FromSeconds(2));
        foreground.IsForeground = false;
        await automation.WaitForCancellationsAsync(1).WaitAsync(TimeSpan.FromSeconds(2));

        foreground.IsForeground = true;
        await automation.WaitForStartsAsync(2).WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class FakeAutomation : IInputAutomation
    {
        public TaskCompletionSource<(AutomationInput Input, TimeSpan Duration)> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task HoldAsync(AutomationInput input, TimeSpan duration, CancellationToken cancellationToken)
        {
            Started.TrySetResult((input, duration));
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class CountingAutomation : IInputAutomation
    {
        private readonly TaskCompletionSource _firstStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _starts;
        private int _cancellations;
        public int StartCount => Volatile.Read(ref _starts);
        public Task WaitForStartsAsync(int count) => count == 1 ? _firstStart.Task : _secondStart.Task;
        public Task WaitForCancellationsAsync(int count) => _firstCancellation.Task;
        public async Task HoldAsync(AutomationInput input, TimeSpan duration, CancellationToken cancellationToken)
        {
            var starts = Interlocked.Increment(ref _starts);
            if (starts >= 1) _firstStart.TrySetResult();
            if (starts >= 2) _secondStart.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref _cancellations) >= 1) _firstCancellation.TrySetResult();
                throw;
            }
        }
    }

    private sealed class FakeForegroundGate(bool isForeground) : IGameForegroundGate
    {
        private int _foreground = isForeground ? 1 : 0;
        public bool IsForeground { get => Volatile.Read(ref _foreground) != 0; set => Volatile.Write(ref _foreground, value ? 1 : 0); }
        public Task WaitUntilForegroundAsync(CancellationToken cancellationToken) => WaitAsync(true, cancellationToken);
        public Task WaitUntilBackgroundAsync(CancellationToken cancellationToken) => WaitAsync(false, cancellationToken);
        private async Task WaitAsync(bool expected, CancellationToken cancellationToken)
        {
            while (IsForeground != expected) await Task.Delay(10, cancellationToken);
        }
    }
}
