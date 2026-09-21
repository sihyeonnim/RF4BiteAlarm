using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Core.Settings;

public sealed record HotkeySetting(FeatureId Feature, KeyStroke[] Keys, int GapMilliseconds);
public sealed record AutoPilkingSetting(AutomationInput Input, double HoldSeconds, double ReleaseSeconds)
{
    public static AutoPilkingSetting Default => new(AutomationInput.MouseRight, 1, 3.5);
}

public sealed record BiteAlarmSetting(SoundCue Sound = SoundCue.Alarm, float Volume = 0.7f)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Sound) || Sound == SoundCue.Tick || !float.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("잘못된 Bite Alarm 소리 또는 음량입니다.");
    }
}

public sealed record VoiceAnnouncementSetting(bool Enabled = true, int Volume = 50)
{
    public void Validate()
    {
        if (Volume is < 0 or > 100)
            throw new ArgumentException("음성 안내 음량은 0–100%여야 합니다.");
    }
}

public sealed class VoiceAnnouncementSettings
{
    private VoiceAnnouncementSetting _current = new();
    public VoiceAnnouncementSetting Current
    {
        get => Volatile.Read(ref _current);
        set { value.Validate(); Volatile.Write(ref _current, value); }
    }
}

public sealed record UserSettings(double Bpm, float Volume, HotkeySetting[] Hotkeys, double? PeriodSeconds = null,
    AutoPilkingSetting? AutoPilking = null, bool ShiftLeftClickHold = false, BiteAlarmSetting? BiteAlarm = null,
    VoiceAnnouncementSetting? VoiceAnnouncement = null, int SettingsFormatVersion = 0)
{
    public const int CurrentSettingsFormatVersion = 2;
    public static UserSettings Default => new(120, 0.5f,
    [
        new(FeatureId.Metronome, [new(0x77, KeyModifiers.Control)], 500),
        new(FeatureId.BiteAlarm, [new(0x78, KeyModifiers.Control)], 500),
        new(FeatureId.AutoPilking, [new(0x79, KeyModifiers.Control)], 500),
        new(FeatureId.PictureInPicture, [new(0x7A, KeyModifiers.Control)], 500),
        new(FeatureId.LeftClickHold, [new(0x7B, KeyModifiers.Control)], 500)
    ], 0.5, SettingsFormatVersion: CurrentSettingsFormatVersion);
    public BiteAlarmSetting EffectiveBiteAlarm => BiteAlarm ?? new();
    public VoiceAnnouncementSetting EffectiveVoiceAnnouncement
    {
        get
        {
            var setting = VoiceAnnouncement ?? new();
            return SettingsFormatVersion < 1 && VoiceAnnouncement is { Volume: 33 }
                ? setting with { Volume = 50 }
                : setting;
        }
    }
    public double EffectivePeriodSeconds => PeriodSeconds ?? 60d / Bpm;
    public AutoPilkingSetting EffectiveAutoPilking
    {
        get
        {
            var setting = AutoPilking ?? AutoPilkingSetting.Default;
            return SettingsFormatVersion < 2 && setting == new AutoPilkingSetting(AutomationInput.MouseRight, 3, 3)
                ? AutoPilkingSetting.Default
                : setting;
        }
    }
    public UserSettings WithMissingDefaultHotkeys()
    {
        var existing = Hotkeys.Select(h => h.Feature).ToHashSet();
        return this with { Hotkeys = Hotkeys.Concat(Default.Hotkeys.Where(h => !existing.Contains(h.Feature))).ToArray() };
    }
    public IReadOnlyList<HotkeyBinding> Bindings() => Hotkeys.Select(h =>
        new HotkeyBinding(h.Keys, TimeSpan.FromMilliseconds(h.GapMilliseconds), new(h.Feature, FeatureAction.Toggle))).ToArray();
    public void Validate()
    {
        if (!double.IsFinite(Bpm) || Bpm is < 1 or > 300 ||
            PeriodSeconds is { } period && (!double.IsFinite(period) || period is < 0.2 or > 60) ||
            !float.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("잘못된 메트로놈 주기, BPM 또는 음량 설정입니다.");
        EffectiveBiteAlarm.Validate();
        EffectiveVoiceAnnouncement.Validate();
        if (SettingsFormatVersion is < 0 or > CurrentSettingsFormatVersion)
            throw new ArgumentException("지원하지 않는 설정 파일 버전입니다.");
        var auto = EffectiveAutoPilking;
        auto.Input.Validate();
        if (!double.IsFinite(auto.HoldSeconds) || auto.HoldSeconds is < 0.1 or > 60 ||
            !double.IsFinite(auto.ReleaseSeconds) || auto.ReleaseSeconds is < 0.1 or > 60)
            throw new ArgumentException("Auto Pilking 누름/해제 시간은 0.1–60초여야 합니다.");
        if (Hotkeys is null || Hotkeys.Any(h => h is null || h.Keys is null || !Enum.IsDefined(h.Feature) || h.Keys.Length > 8 ||
            h.GapMilliseconds is < 100 or > 5000) ||
            Hotkeys.Select(h => h.Feature).Distinct().Count() != Hotkeys.Length)
            throw new ArgumentException("잘못된 단축키 설정입니다.");
        new HotkeyMatcher().SetBindings(Bindings());
    }
}
