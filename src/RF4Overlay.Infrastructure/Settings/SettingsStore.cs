using System.Text.Json;
using System.Threading.Channels;
using RF4Overlay.Core.Settings;

namespace RF4Overlay.Infrastructure.Settings;

/// <summary>Coalesces pending edits and atomically writes settings without blocking the UI thread.</summary>
public sealed class SettingsStore : IAsyncDisposable
{
    private readonly string _path;
    private readonly Channel<UserSettings> _pending = Channel.CreateBounded<UserSettings>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _writer;
    public event EventHandler<string>? SaveFailed;
    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RF4Overlay", "settings.json");
        _writer = Task.Run(WriteAsync);
    }
    public UserSettings Load()
    {
        if (!File.Exists(_path)) return UserSettings.Default;
        var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path)) ?? throw new InvalidDataException("설정 파일이 비어 있습니다.");
        settings.Validate();
        return settings;
    }
    public void Save(UserSettings settings)
    {
        settings.Validate();
        if (!_pending.Writer.TryWrite(settings)) throw new ObjectDisposedException(nameof(SettingsStore));
    }
    private async Task WriteAsync()
    {
        await foreach (var settings in _pending.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
                File.Move(temp, _path, true);
            }
            catch (Exception error)
            {
                foreach (EventHandler<string> handler in SaveFailed?.GetInvocationList() ?? [])
                {
                    try { handler(this, error.Message); }
                    catch (Exception observerError) { System.Diagnostics.Trace.TraceError(observerError.ToString()); }
                }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }
}
