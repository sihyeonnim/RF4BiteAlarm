using System.ComponentModel;
using System.Runtime.InteropServices;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Infrastructure.Input;

public sealed class WindowsInputAutomation : IInputAutomation, ILeftButtonHoldAutomation
{
    public async Task HoldAsync(AutomationInput input, TimeSpan duration, CancellationToken cancellationToken)
    {
        input.Validate();
        if (duration < TimeSpan.FromSeconds(0.1) || duration > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(duration));
        cancellationToken.ThrowIfCancellationRequested();
        Send(input, down: true);
        try { await Task.Delay(duration, cancellationToken).ConfigureAwait(false); }
        finally { Send(input, down: false); }
    }

    public ValueTask<IAsyncDisposable> HoldLeftButtonAsync(bool withShift, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shiftDown = false;
        try
        {
            if (withShift)
            {
                SendKeyboard(0x10, down: true);
                shiftDown = true;
            }
            SendMouse(0x0002);
            return new(new LeftButtonHold(withShift));
        }
        catch
        {
            if (shiftDown) SendKeyboard(0x10, down: false);
            throw;
        }
    }

    private static void Send(AutomationInput input, bool down)
    {
        if (input.Kind == AutomationInputKind.MouseRight) SendMouse(down ? 0x0008u : 0x0010u);
        else SendKeyboard(input.VirtualKey, down);
    }

    private static void SendMouse(uint flags) => SendNative(
        new INPUT { Type = 0, Data = new() { Mouse = new() { Flags = flags } } });

    private static void SendKeyboard(byte virtualKey, bool down) => SendNative(
        new INPUT { Type = 1, Data = new() { Keyboard = new() { VirtualKey = virtualKey, Flags = down ? 0u : 0x0002u } } });

    private static void SendNative(INPUT native)
    {
        if (SendInput(1, [native], Marshal.SizeOf<INPUT>()) != 1) throw new Win32Exception();
    }

    private sealed class LeftButtonHold(bool withShift) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
            try { SendMouse(0x0004); }
            finally { if (withShift) SendKeyboard(0x10, down: false); }
            return ValueTask.CompletedTask;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] INPUT[] inputs, int size);
}
