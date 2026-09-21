using System.Threading.Channels;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Features.LeftClickHold;

public sealed class LeftClickHoldSettings
{
    private int _withShift;
    public bool WithShift
    {
        get => Volatile.Read(ref _withShift) != 0;
        set => Volatile.Write(ref _withShift, value ? 1 : 0);
    }
}

public sealed record LeftClickHoldOptions(TimeSpan DoubleClickMaximumGap, int MaximumXDistance, int MaximumYDistance)
{
    public static LeftClickHoldOptions Default { get; } = new(TimeSpan.FromMilliseconds(500), 8, 8);

    public void Validate()
    {
        if (DoubleClickMaximumGap <= TimeSpan.Zero || MaximumXDistance < 0 || MaximumYDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(DoubleClickMaximumGap));
    }
}

public sealed class LeftClickHoldFeature(
    IMouseInputSource input,
    ILeftButtonHoldAutomation automation,
    LeftClickHoldSettings settings,
    LeftClickHoldOptions options,
    IGameForegroundGate foreground) : IFeature
{
    public FeatureStatus InitialStatus { get; } = new(
        FeatureId.LeftClickHold,
        "Double Click to Holding",
        FeatureState.Stopped,
        "더블 좌클릭 후 좌클릭을 유지하고 다음 좌클릭에서 해제합니다.");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        options.Validate();
        var events = Channel.CreateUnbounded<MouseButtonInput?>(new UnboundedChannelOptions { SingleReader = true });
        void OnButton(object? sender, MouseButtonInput value) => events.Writer.TryWrite(value);
        void OnReset(object? sender, EventArgs args) => events.Writer.TryWrite(null);
        input.MouseButtonReceived += OnButton;
        input.InputReset += OnReset;
        using var focusWatcherCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var focusWatcher = WatchFocusAsync(focusWatcherCancellation.Token);
        IAsyncDisposable? held = null;
        MouseButtonInput? firstUp = null;
        var ignoreNextUp = false;
        try
        {
            await foreach (var current in events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (current is null)
                {
                    firstUp = null;
                    ignoreNextUp = false;
                    if (held is not null) { await held.DisposeAsync().ConfigureAwait(false); held = null; }
                    continue;
                }
                if (!foreground.IsForeground)
                {
                    firstUp = null;
                    ignoreNextUp = false;
                    if (held is not null) { await held.DisposeAsync().ConfigureAwait(false); held = null; }
                    continue;
                }
                if (current.Button != MouseButton.Left) continue;
                if (held is not null)
                {
                    if (current.IsDown)
                    {
                        await held.DisposeAsync().ConfigureAwait(false);
                        held = null;
                        ignoreNextUp = true;
                    }
                    continue;
                }
                if (ignoreNextUp)
                {
                    if (!current.IsDown) ignoreNextUp = false;
                    continue;
                }
                if (current.IsDown) continue;
                if (firstUp is { } first && IsDoubleClick(first, current, options))
                {
                    firstUp = null;
                    held = await automation.HoldLeftButtonAsync(settings.WithShift, cancellationToken).ConfigureAwait(false);
                }
                else firstUp = current;
            }
        }
        finally
        {
            input.MouseButtonReceived -= OnButton;
            input.InputReset -= OnReset;
            events.Writer.TryComplete();
            if (held is not null) await held.DisposeAsync().ConfigureAwait(false);
            focusWatcherCancellation.Cancel();
            try { await focusWatcher.ConfigureAwait(false); }
            catch (OperationCanceledException) when (focusWatcherCancellation.IsCancellationRequested) { }
        }

        async Task WatchFocusAsync(CancellationToken watcherToken)
        {
            while (true)
            {
                await foreground.WaitUntilBackgroundAsync(watcherToken).ConfigureAwait(false);
                events.Writer.TryWrite(null);
                await foreground.WaitUntilForegroundAsync(watcherToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsDoubleClick(MouseButtonInput first, MouseButtonInput second, LeftClickHoldOptions options) =>
        second.Timestamp >= first.Timestamp && second.Timestamp - first.Timestamp <= options.DoubleClickMaximumGap &&
        Math.Abs(second.X - first.X) <= options.MaximumXDistance &&
        Math.Abs(second.Y - first.Y) <= options.MaximumYDistance;
}

public sealed class UnavailableLeftClickHoldFeature() : PlannedFeature(
    FeatureId.LeftClickHold, "Double Click to Holding", "전역 마우스 입력 서비스가 필요합니다.");
