using System.Diagnostics;
using System.Runtime.InteropServices;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Infrastructure.Input;

public sealed class Rf4ForegroundGate : IGameForegroundGate
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public bool IsForeground
    {
        get
        {
            var handle = GetForegroundWindow();
            if (handle == 0) return false;
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0) return false;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return IsNativeRf4Process(process.ProcessName);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    public Task WaitUntilForegroundAsync(CancellationToken cancellationToken) => WaitForAsync(true, cancellationToken);
    public Task WaitUntilBackgroundAsync(CancellationToken cancellationToken) => WaitForAsync(false, cancellationToken);

    private async Task WaitForAsync(bool expected, CancellationToken cancellationToken)
    {
        while (IsForeground != expected)
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsNativeRf4Process(string name) =>
        name.Equals("rf4_x64", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("rf4", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RussianFishing4", StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
