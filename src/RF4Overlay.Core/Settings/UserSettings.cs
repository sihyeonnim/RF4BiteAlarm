using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Core.Settings;

public sealed record HotkeySetting(FeatureId Feature, KeyStroke[] Keys, int GapMilliseconds);
public sealed record UserSettings(int Bpm, float Volume, HotkeySetting[] Hotkeys)
{
    public static UserSettings Default => new(120, 0.5f,
    [
        new(FeatureId.Metronome, [new(0x77, KeyModifiers.Control)], 500),
        new(FeatureId.BiteAlarm, [new(0x78, KeyModifiers.Control)], 500),
        new(FeatureId.AutoPilking, [new(0x79, KeyModifiers.Control)], 500)
    ]);
    public IReadOnlyList<HotkeyBinding> Bindings() => Hotkeys.Select(h =>
        new HotkeyBinding(h.Keys, TimeSpan.FromMilliseconds(h.GapMilliseconds), new(h.Feature, FeatureAction.Toggle))).ToArray();
    public void Validate()
    {
        if (Bpm is < 20 or > 300 || !float.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("잘못된 BPM 또는 음량 설정입니다.");
        if (Hotkeys is null || Hotkeys.Any(h => h is null || h.Keys is null || !Enum.IsDefined(h.Feature) || h.Keys.Length > 8 ||
            h.GapMilliseconds is < 100 or > 5000) ||
            Hotkeys.Select(h => h.Feature).Distinct().Count() != Hotkeys.Length)
            throw new ArgumentException("잘못된 단축키 설정입니다.");
        new HotkeyMatcher().SetBindings(Bindings());
    }
}
