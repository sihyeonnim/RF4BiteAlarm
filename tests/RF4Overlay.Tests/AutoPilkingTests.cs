using RF4Overlay.Core.Input;
using RF4Overlay.Features.AutoPilking;

namespace RF4Overlay.Tests;

public sealed class AutoPilkingTests
{
    [Fact]
    public void SettingsDefaultToRightMouseAndThreeSecondCycle()
    {
        var settings = new AutoPilkingSettings();
        var cycle = settings.Snapshot();

        Assert.Equal(AutomationInput.MouseRight, cycle.Input);
        Assert.Equal(TimeSpan.FromSeconds(3), cycle.HoldDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), cycle.ReleaseDuration);
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
        var feature = new AutoPilkingFeature(automation, settings);
        using var cancellation = new CancellationTokenSource();

        var run = feature.RunAsync(cancellation.Token);
        var call = await automation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutomationInput.Keyboard((byte)'P'), call.Input);
        Assert.Equal(TimeSpan.FromSeconds(0.4), call.Duration);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(automation.CancellationObserved);
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
}
