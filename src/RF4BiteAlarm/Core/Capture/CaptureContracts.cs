namespace RF4Overlay.Core.Capture;

/// <summary>Owned CPU copy of a BGRA8 frame; coordinates are relative to this frame.</summary>
public sealed record CapturedFrame(int Width, int Height, int Stride, ReadOnlyMemory<byte> Pixels);

/// <summary>Windows Graphics Capture will implement this in Infrastructure.</summary>
public interface IWindowCapture : IAsyncDisposable
{
    IAsyncEnumerable<CapturedFrame> CaptureAsync(nint windowHandle, CancellationToken cancellationToken);
}

/// <summary>A shared stream of the latest captured game frames.</summary>
public interface ICaptureFrameSource
{
    IAsyncEnumerable<CapturedFrame> ReadFramesAsync(CancellationToken cancellationToken);
}

/// <summary>Presents the shared game capture without owning another capture session.</summary>
public interface IPictureInPicturePresenter
{
    Task ShowAsync(ICaptureFrameSource frames, CancellationToken cancellationToken);
}

public interface IAlarmSound
{
    Task PlayAsync(CancellationToken cancellationToken);
}
