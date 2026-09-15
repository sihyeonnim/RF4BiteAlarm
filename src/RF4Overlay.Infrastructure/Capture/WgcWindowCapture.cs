using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using RF4Overlay.Core.Capture;

namespace RF4Overlay.Infrastructure.Capture;

/// <summary>One active stream per instance; iterator owns and releases every native resource.</summary>
public sealed class WgcWindowCapture : IWindowCapture
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _disposeLock = new();
    private Task? _dispose;
    private int _disposed;
    public async IAsyncEnumerable<CapturedFrame> CaptureAsync(nint windowHandle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture를 사용할 수 없습니다.");
            if (!GameWindowLocator.IsWindow(windowHandle)) throw new InvalidOperationException("캡처할 창이 종료되었습니다.");
            using var device = Direct3DInterop.CreateDevice();
            var item = Direct3DInterop.CreateItem(windowHandle);
            var size = item.Size;
            if (size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException("캡처할 창 크기가 0입니다.");
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            using var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            var closed = 0;
            void OnClosed(GraphicsCaptureItem sender, object args) => Interlocked.Exchange(ref closed, 1);
            item.Closed += OnClosed;
            try
            {
                session.StartCapture();
                var lastFrame = Stopwatch.StartNew();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref closed) != 0 || !GameWindowLocator.IsWindow(windowHandle))
                        throw new InvalidOperationException("캡처할 창이 종료되었습니다.");
                    if (GameWindowLocator.IsMinimized(windowHandle))
                        throw new InvalidOperationException("창이 최소화되었습니다. 복원하면 다시 연결합니다.");
                    CapturedFrame? result = null;
                    var resized = false;
                    using (var frame = pool.TryGetNextFrame())
                    {
                        if (frame is not null)
                        {
                            var current = frame.ContentSize;
                            if (current.Width != size.Width || current.Height != size.Height)
                            {
                                size = current;
                                resized = true;
                                // Dispose this frame before recreating the pool below.
                            }
                            else
                            {
                                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask(token).ConfigureAwait(false);
                                var stride = checked(bitmap.PixelWidth * 4);
                                var bytes = new byte[checked(stride * bitmap.PixelHeight)];
                                bitmap.CopyToBuffer(bytes.AsBuffer());
                                result = new(bitmap.PixelWidth, bitmap.PixelHeight, stride, bytes);
                                lastFrame.Restart();
                            }
                        }
                    }
                    if (result is not null) yield return result;
                    else if (resized && size.Width > 0 && size.Height > 0)
                    {
                        // Recreate only after a size change; same-size frame starvation is handled by timeout.
                        pool.Recreate(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                    }
                    if (lastFrame.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("5초 동안 새 프레임을 받지 못했습니다.");
                    await Task.Delay(33, token).ConfigureAwait(false);
                }
            }
            finally { item.Closed -= OnClosed; }
        }
        finally { _gate.Release(); }
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            Interlocked.Exchange(ref _disposed, 1);
            return new(_dispose ??= DisposeCoreAsync());
        }
    }
    private async Task DisposeCoreAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _lifetime.Dispose();
    }
}
