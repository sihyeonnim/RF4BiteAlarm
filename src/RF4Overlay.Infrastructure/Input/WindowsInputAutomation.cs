using System.ComponentModel;
using System.Runtime.InteropServices;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Infrastructure.Input;

public sealed class WindowsInputAutomation : IInputAutomation
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

    private static void Send(AutomationInput input, bool down)
    {
        var native = input.Kind == AutomationInputKind.MouseRight
            ? new INPUT { Type = 0, Data = new() { Mouse = new() { Flags = down ? 0x0008u : 0x0010u } } }
            : new INPUT { Type = 1, Data = new() { Keyboard = new() { VirtualKey = input.VirtualKey, Flags = down ? 0u : 0x0002u } } };
        if (SendInput(1, [native], Marshal.SizeOf<INPUT>()) != 1) throw new Win32Exception();
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
