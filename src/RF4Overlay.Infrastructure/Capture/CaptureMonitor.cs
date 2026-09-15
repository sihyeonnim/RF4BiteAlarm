using RF4Overlay.Core.Capture;

namespace RF4Overlay.Infrastructure.Capture;

/// <summary>Diagnostic capture monitor. Re-enumerates after window loss or device failure.</summary>
public sealed class CaptureMonitor : IAsyncDisposable
{
    private readonly IGameWindowLocator _locator;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _run;
    private CapturedFrame? _latest;
    public event EventHandler<CaptureStatus>? StatusChanged;
    public CapturedFrame? LatestFrame => Volatile.Read(ref _latest);
    public CaptureMonitor(IGameWindowLocator locator) => _locator = locator;
    public void Start() => _run ??= Task.Run(RunAsync);
    private async Task RunAsync()
    {
        var token = _cancel.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var window = _locator.FindWindows().FirstOrDefault();
                if (window is null)
                {
                    Report(new("RF4 창을 찾지 못했습니다. 실행하면 자동 연결합니다."));
                }
                else
                {
                    var label = window.IsStreaming ? "RF4 Steam 스트리밍 창" : "RF4";
                    Report(new(label + " 연결 중"));
                    await using var capture = new WgcWindowCapture();
                    long count = 0;
                    await foreach (var frame in capture.CaptureAsync(window.Handle, token))
                    {
                        if (!_locator.IsCurrent(window)) throw new InvalidOperationException("RF4 창이 교체되었습니다.");
                        Volatile.Write(ref _latest, frame);
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
    private void Report(CaptureStatus status) => StatusChanged?.Invoke(this, status);
    public async ValueTask DisposeAsync()
    {
        await _cancel.CancelAsync().ConfigureAwait(false);
        if (_run is not null) await _run.ConfigureAwait(false);
        _cancel.Dispose();
    }
}
