using System.Text.Json;
using System.IO;
using RF4Overlay.Core.Settings;

namespace RF4BiteAlarm.Infrastructure;

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RF4BiteAlarm", "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return AppSettings.Default;
        var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("설정 파일이 비어 있습니다.");
        value.Validate();
        return value;
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }
}
