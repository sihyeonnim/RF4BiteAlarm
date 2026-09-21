using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RF4Overlay.Core.Capture;

namespace RF4Overlay.Infrastructure.Capture;

public sealed class GameWindowLocator : IGameWindowLocator
{
    public IReadOnlyList<CaptureWindow> FindWindows()
    {
        var found = new List<CaptureWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var name = process.ProcessName;
                var text = new StringBuilder(1024);
                GetWindowText(handle, text, text.Capacity);
                var title = text.ToString();
                var streaming = name.Equals("streaming_client", StringComparison.OrdinalIgnoreCase) &&
                    title.Contains("Russian Fishing 4", StringComparison.OrdinalIgnoreCase);
                var native = name.Equals("rf4_x64", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("rf4", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("RussianFishing4", StringComparison.OrdinalIgnoreCase);
                if ((native || streaming) && GetClientRect(handle, out var rect))
                    found.Add(new(handle, (int)pid, name, title, rect.Right, rect.Bottom, streaming));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        return found.OrderBy(w => w.IsStreaming).ToArray();
    }
    public bool IsCurrent(CaptureWindow window)
    {
        GetWindowThreadProcessId(window.Handle, out var pid);
        return pid == window.ProcessId && IsWindow(window.Handle);
    }
    internal static bool IsMinimized(nint window) => IsIconic(window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Rect rect);
}
