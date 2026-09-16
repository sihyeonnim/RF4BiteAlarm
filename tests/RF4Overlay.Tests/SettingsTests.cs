using System.Text.Json;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Core.Settings;

namespace RF4Overlay.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void SettingsRoundTripPreservesRepeatedKeysModifiersAndTiming()
    {
        var settings = new UserSettings(80, 0.3f,
            [new(FeatureId.Metronome, [new(97, KeyModifiers.Control), new(97, KeyModifiers.Control)], 600)], 0.75);
        var copy = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        copy.Validate();
        Assert.Equal(80, copy.Bpm);
        Assert.Equal(0.75, copy.EffectivePeriodSeconds);
        var binding = Assert.Single(copy.Bindings());
        Assert.Equal(TimeSpan.FromMilliseconds(600), binding.MaximumGap);
        Assert.Equal(2, binding.Sequence.Count);
        Assert.All(binding.Sequence, key => Assert.Equal(KeyModifiers.Control, key.Modifiers));
    }

    [Fact]
    public void InvalidSettingsFailBeforeReplacingWorkingBindings()
    {
        UserSettings.Default.Validate();
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with { Bpm = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with { PeriodSeconds = 0.1 }).Validate());
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with { Volume = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => new UserSettings(120, 0.5f,
            [new(FeatureId.Metronome, [new(65)], 500), new(FeatureId.BiteAlarm, [new(65), new(65)], 500)]).Validate());
        Assert.Throws<ArgumentException>(() => new UserSettings(120, 0.5f,
            [new(FeatureId.Metronome, [new(0x11)], 500)]).Validate());
    }

    [Fact]
    public void LegacySettingsWithoutPeriodUseBpm()
    {
        var legacy = JsonSerializer.Deserialize<UserSettings>("""{"Bpm":150,"Volume":0.3,"Hotkeys":[]}""")!;
        legacy.Validate();
        Assert.Null(legacy.PeriodSeconds);
        Assert.Equal(0.4, legacy.EffectivePeriodSeconds, 10);
    }
}
