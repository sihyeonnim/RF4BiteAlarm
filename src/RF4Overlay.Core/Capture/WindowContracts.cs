namespace RF4Overlay.Core.Capture;

public sealed record CaptureWindow(nint Handle, int ProcessId, string ProcessName, string Title, int Width, int Height, bool IsStreaming);
public interface IGameWindowLocator
{
    IReadOnlyList<CaptureWindow> FindWindows();
    bool IsCurrent(CaptureWindow window);
}
public sealed record CaptureStatus(string Message, long FrameCount = 0, int Width = 0, int Height = 0);
