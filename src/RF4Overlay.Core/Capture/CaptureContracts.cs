namespace RF4Overlay.Core.Capture;

/// <summary>Owned CPU copy of a BGRA8 frame; coordinates are relative to this frame.</summary>
public sealed record CapturedFrame(int Width, int Height, int Stride, ReadOnlyMemory<byte> Pixels);

/// <summary>Windows Graphics Capture will implement this in Infrastructure.</summary>
public interface IWindowCapture : IAsyncDisposable
{
    IAsyncEnumerable<CapturedFrame> CaptureAsync(nint windowHandle, CancellationToken cancellationToken);
}

public interface IAlarmSound
{
    Task PlayAsync(CancellationToken cancellationToken);
}
