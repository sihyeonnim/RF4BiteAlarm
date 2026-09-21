using RF4Overlay.Core.Input;
using RF4Overlay.Features.LeftClickHold;

namespace RF4Overlay.Tests;

public sealed class LeftClickHoldTests
{
    [Fact]
    public async Task IgnoresClicksOutsideRf4ReleasesOnFocusLossAndWorksAgainOnReturn()
    {
        var input = new FakeMouseInput();
        var automation = new FakeLeftHoldAutomation();
        var foreground = new FakeForegroundGate(false);
        var feature = new LeftClickHoldFeature(input, automation, new(), LeftClickHoldOptions.Default, foreground);
        using var cancellation = new CancellationTokenSource();
        var run = feature.RunAsync(cancellation.Token);
        await Task.Delay(30);

        SendDoubleClick(input, TimeSpan.Zero);
        await Task.Delay(50);
        Assert.Equal(0, automation.HoldCount);

        foreground.IsForeground = true;
        await Task.Delay(30);
        SendDoubleClick(input, TimeSpan.FromSeconds(1));
        await automation.FirstHold.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, automation.HoldCount);

        foreground.IsForeground = false;
        await automation.FirstRelease.Task.WaitAsync(TimeSpan.FromSeconds(2));

        foreground.IsForeground = true;
        await Task.Delay(30);
        SendDoubleClick(input, TimeSpan.FromSeconds(2));
        await automation.SecondHold.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, automation.HoldCount);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await automation.SecondRelease.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static void SendDoubleClick(FakeMouseInput input, TimeSpan start)
    {
        input.Send(new(MouseButton.Left, false, 100, 100, start));
        input.Send(new(MouseButton.Left, false, 102, 101, start + TimeSpan.FromMilliseconds(100)));
    }

    private sealed class FakeMouseInput : IMouseInputSource
    {
        public event EventHandler<MouseButtonInput>? MouseButtonReceived;
        public event EventHandler? InputReset;
        public void Send(MouseButtonInput input) => MouseButtonReceived?.Invoke(this, input);
        public void Reset() => InputReset?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeLeftHoldAutomation : ILeftButtonHoldAutomation
    {
        public TaskCompletionSource FirstHold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondHold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _holds;
        public int HoldCount => Volatile.Read(ref _holds);

        public ValueTask<IAsyncDisposable> HoldLeftButtonAsync(bool withShift, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hold = Interlocked.Increment(ref _holds);
            (hold == 1 ? FirstHold : SecondHold).TrySetResult();
            return new(new Handle(hold == 1 ? FirstRelease : SecondRelease));
        }

        private sealed class Handle(TaskCompletionSource released) : IAsyncDisposable
        {
            private int _disposed;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) released.TrySetResult();
                return ValueTask.CompletedTask;
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
