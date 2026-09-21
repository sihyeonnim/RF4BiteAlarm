using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Infrastructure.Input;

/// <summary>Owns low-level hooks on a dedicated message-pump thread. Never suppresses input.</summary>
public sealed class WindowsInputMonitor : IUserInputSource, IMouseInputSource, IAsyncDisposable
{
    private readonly Channel<(KeyInput? Key, MouseButtonInput? Mouse, bool Injected)> _events =
        Channel.CreateBounded<(KeyInput?, MouseButtonInput?, bool)>(
        new BoundedChannelOptions(2048) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HookProc _keyboard, _mouse;
    private readonly object _lifecycle = new();
    private Thread? _thread;
    private Task? _consumer, _dispose;
    private uint _threadId;
    private int _overflow;
    public event EventHandler<KeyInput>? KeyReceived;
    public event EventHandler<MouseButtonInput>? MouseButtonReceived;
    public event EventHandler<UserInput>? InputReceived;
    public event EventHandler? InputReset;

    public WindowsInputMonitor() { _keyboard = Keyboard; _mouse = Mouse; }

    public Task StartAsync()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            if (_thread is not null) return _ready.Task;
            _consumer = Task.Run(ConsumeAsync);
            _thread = new Thread(Pump) { IsBackground = true, Name = "RF4 input monitor" };
            _thread.Start();
            return _ready.Task;
        }
    }

    private void Pump()
    {
        nint keyboard = 0, mouse = 0;
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0); // Ensure queue exists before publishing ready.
            keyboard = SetWindowsHookEx(13, _keyboard, GetModuleHandle(null), 0);
            if (keyboard == 0) throw new Win32Exception();
            mouse = SetWindowsHookEx(14, _mouse, GetModuleHandle(null), 0);
            if (mouse == 0) throw new Win32Exception();
            _ready.TrySetResult();
            int result;
            while ((result = GetMessage(out var msg, 0, 0, 0)) > 0)
            { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            if (result < 0) throw new Win32Exception();
        }
        catch (Exception error) { _ready.TrySetException(error); Trace.TraceError(error.ToString()); }
        finally
        {
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            _events.Writer.TryComplete();
            _ended.TrySetResult();
        }
    }

    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KeyboardData>(data);
            var down = message == 0x100 || message == 0x104;
            var injected = (info.Flags & 0x12) != 0;
            Publish((new((byte)info.Key, down, injected, Stopwatch.GetElapsedTime(0)), null, injected));
        }
        return CallNextHookEx(0, code, message, data);
    }
    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<MouseData>(data);
            MouseButtonInput? button = message switch
            {
                0x0201 => new(MouseButton.Left, true, info.X, info.Y, Stopwatch.GetElapsedTime(0)),
                0x0202 => new(MouseButton.Left, false, info.X, info.Y, Stopwatch.GetElapsedTime(0)),
                _ => null
            };
            Publish((null, button, (info.Flags & 3) != 0));
        }
        return CallNextHookEx(0, code, message, data);
    }
    private void Publish((KeyInput? Key, MouseButtonInput? Mouse, bool Injected) value)
    {
        if (!_events.Writer.TryWrite(value)) Interlocked.Exchange(ref _overflow, 1);
    }
    private async Task ConsumeAsync()
    {
        await foreach (var value in _events.Reader.ReadAllAsync())
        {
            try
            {
                if (Interlocked.Exchange(ref _overflow, 0) != 0) InputReset?.Invoke(this, EventArgs.Empty);
                if (!InputPolicy.IsPhysical(value.Injected)) continue;
                InputReceived?.Invoke(this, new(DateTimeOffset.UtcNow));
                if (value.Key is not null) KeyReceived?.Invoke(this, value.Key);
                if (value.Mouse is not null) MouseButtonReceived?.Invoke(this, value.Mouse);
            }
            catch (Exception error) { Trace.TraceError(error.ToString()); }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifecycle) return new(_dispose ??= StopAsync());
    }
    private async Task StopAsync()
    {
        if (_thread is null) return;
        try { await _ready.Task.ConfigureAwait(false); } catch { }
        if (!_ended.Task.IsCompleted && !PostThreadMessage(_threadId, 0x12, 0, 0)) throw new Win32Exception();
        await _ended.Task.ConfigureAwait(false);
        if (_consumer is not null) await _consumer.ConfigureAwait(false);
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message msg, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message msg, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message msg);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint id, uint message, nuint wParam, nint lParam);
}
