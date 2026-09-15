using System.Text.Json;
using RF4Overlay.Core.Settings;

namespace RF4Overlay.Infrastructure.Settings;

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RF4Overlay", "settings.json");
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
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
