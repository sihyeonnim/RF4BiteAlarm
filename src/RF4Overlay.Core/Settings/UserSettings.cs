using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Core.Settings;

public sealed record HotkeySetting(FeatureId Feature, KeyStroke[] Keys, int GapMilliseconds);
public sealed record AutoPilkingSetting(AutomationInput Input, double HoldSeconds, double ReleaseSeconds)
{
    public static AutoPilkingSetting Default => new(AutomationInput.MouseRight, 3, 3);
}

public sealed record UserSettings(double Bpm, float Volume, HotkeySetting[] Hotkeys, double? PeriodSeconds = null,
    AutoPilkingSetting? AutoPilking = null)
{
    public static UserSettings Default => new(120, 0.5f,
    [
        new(FeatureId.Metronome, [new(0x77, KeyModifiers.Control)], 500),
        new(FeatureId.BiteAlarm, [new(0x78, KeyModifiers.Control)], 500),
        new(FeatureId.AutoPilking, [new(0x79, KeyModifiers.Control)], 500)
    ], 0.5);
    public double EffectivePeriodSeconds => PeriodSeconds ?? 60d / Bpm;
    public AutoPilkingSetting EffectiveAutoPilking => AutoPilking ?? AutoPilkingSetting.Default;
    public IReadOnlyList<HotkeyBinding> Bindings() => Hotkeys.Select(h =>
        new HotkeyBinding(h.Keys, TimeSpan.FromMilliseconds(h.GapMilliseconds), new(h.Feature, FeatureAction.Toggle))).ToArray();
    public void Validate()
    {
        if (!double.IsFinite(Bpm) || Bpm is < 1 or > 300 ||
            PeriodSeconds is { } period && (!double.IsFinite(period) || period is < 0.2 or > 60) ||
            !float.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("잘못된 메트로놈 주기, BPM 또는 음량 설정입니다.");
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
