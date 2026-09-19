using RF4Overlay.Core.Capture;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace RF4Overlay.Infrastructure.Capture;

/// <summary>Diagnostic capture monitor. Re-enumerates after window loss or device failure.</summary>
public sealed class CaptureMonitor : ICaptureFrameSource, IAsyncDisposable
{
    private readonly IGameWindowLocator _locator;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _run;
    private Task? _dispose;
    private readonly object _lifecycle = new();
    private readonly object _subscriptionsLock = new();
    private readonly HashSet<Channel<CapturedFrame>> _subscriptions = [];
    private CapturedFrame? _latest;
    public event EventHandler<CaptureStatus>? StatusChanged;
    public CapturedFrame? LatestFrame => Volatile.Read(ref _latest);
    public CaptureMonitor(IGameWindowLocator locator) => _locator = locator;

    public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            lock (_subscriptionsLock)
            {
                _subscriptions.Add(channel);
                if (LatestFrame is { } latest) channel.Writer.TryWrite(latest);
            }
        }
        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return frame;
        }
        finally
        {
            lock (_subscriptionsLock) _subscriptions.Remove(channel);
            channel.Writer.TryComplete();
        }
    }
    public void Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            _run ??= Task.Run(RunAsync);
        }
    }
    private async Task RunAsync()
    {
        var token = _cancel.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var candidates = _locator.FindWindows();
                var window = candidates.FirstOrDefault(candidate => !candidate.IsStreaming);
                if (window is null)
                {
                    Report(new(candidates.Any(candidate => candidate.IsStreaming)
                        ? "RF4 본체 없음 · Steam 스트리밍 제목만으로 게임을 확인할 수 없습니다."
                        : "RF4 창을 찾지 못했습니다. 실행하면 자동 연결합니다."));
                }
                else
                {
                    const string label = "RF4";
                    Report(new(label + " 연결 중"));
                    await using var capture = new WgcWindowCapture();
                    long count = 0;
                    await foreach (var frame in capture.CaptureAsync(window.Handle, token))
                    {
                        if (!_locator.IsCurrent(window)) throw new InvalidOperationException("RF4 창이 교체되었습니다.");
                        Volatile.Write(ref _latest, frame);
                        Publish(frame);
                        count++;
                        if (count == 1 || count % 15 == 0) Report(new(label + " 캡처 중", count, frame.Width, frame.Height));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { Report(new("캡처 재연결 대기: " + error.Message)); }
            Volatile.Write(ref _latest, null);
            try { await Task.Delay(2000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        Volatile.Write(ref _latest, null);
    }
    private void Report(CaptureStatus status)
    {
        foreach (EventHandler<CaptureStatus> handler in StatusChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, status); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        }
    }
    private void Publish(CapturedFrame frame)
    {
        lock (_subscriptionsLock)
            foreach (var channel in _subscriptions) channel.Writer.TryWrite(frame);
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifecycle) return new(_dispose ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        await _cancel.CancelAsync().ConfigureAwait(false);
        if (_run is not null) await _run.ConfigureAwait(false);
        lock (_subscriptionsLock)
        {
            foreach (var channel in _subscriptions) channel.Writer.TryComplete();
            _subscriptions.Clear();
        }
        _cancel.Dispose();
    }
}
