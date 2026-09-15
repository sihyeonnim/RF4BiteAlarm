using System.Threading.Channels;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Infrastructure.Input;

public sealed class GlobalHotkeyService : IAsyncDisposable
{
    private readonly WindowsInputMonitor _monitor;
    private readonly FeatureCommandDispatcher _runtime;
    private readonly HotkeyMatcher _matcher = new();
    private readonly Channel<FeatureCommand> _commands = Channel.CreateBounded<FeatureCommand>(32);
    private readonly Task _consumer;
    private int _disposed;
    public event EventHandler<string>? Error;
    public bool Suspended { get; set; }
    public GlobalHotkeyService(WindowsInputMonitor monitor, FeatureCommandDispatcher runtime)
    {
        _monitor = monitor; _runtime = runtime;
        monitor.KeyReceived += OnKey;
        monitor.InputReset += OnReset;
        _consumer = ConsumeAsync();
    }
    public void SetBindings(IEnumerable<HotkeyBinding> bindings) => _matcher.SetBindings(bindings);
    private void OnReset(object? sender, EventArgs args) => _matcher.Reset();
    private void OnKey(object? sender, KeyInput input)
    {
        if (Suspended) { _matcher.Reset(); return; }
        var command = _matcher.Process(input);
        if (command is not null && !_commands.Writer.TryWrite(command)) Error?.Invoke(this, "단축키 명령 대기열이 가득 찼습니다.");
    }
    private async Task ConsumeAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            if (Volatile.Read(ref _disposed) != 0) break;
            var result = await _runtime.ExecuteAsync(command).ConfigureAwait(false);
            if (!result.Succeeded) Error?.Invoke(this, result.Message);
        }
    }
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _monitor.KeyReceived -= OnKey; _monitor.InputReset -= OnReset;
        _commands.Writer.TryComplete();
        await _consumer.ConfigureAwait(false);
    }
}
