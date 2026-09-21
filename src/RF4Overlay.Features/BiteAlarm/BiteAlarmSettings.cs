using RF4Overlay.Core.Settings;

namespace RF4Overlay.Features.BiteAlarm;

public sealed class BiteAlarmSettings
{
    private BiteAlarmSetting _current = new();
    public BiteAlarmSetting Current
    {
        get => Volatile.Read(ref _current);
        set { value.Validate(); Volatile.Write(ref _current, value); }
    }
}
