using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Core.Settings;

public sealed record BiteAlarmSetting(SoundCue Sound = SoundCue.Alarm, float Volume = 0.7f)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Sound) || Sound == SoundCue.Tick || !float.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("잘못된 알람 소리 또는 음량입니다.");
    }
}

public sealed record AppSettings(BiteAlarmSetting Alarm, KeyStroke Hotkey)
{
    public static AppSettings Default => new(new(), new(0x78, KeyModifiers.Control));

    public void Validate()
    {
        Alarm.Validate();
        if (Hotkey.VirtualKey is 0 or 255 || InputPolicy.IsModifier(Hotkey.VirtualKey))
            throw new ArgumentException("잘못된 단축키입니다.");
    }
}
