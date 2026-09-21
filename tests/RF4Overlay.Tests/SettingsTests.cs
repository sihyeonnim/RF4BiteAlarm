using System.Text.Json;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Core.Settings;

namespace RF4Overlay.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void VoiceAnnouncementDefaultsAndRoundTripAreValid()
    {
        Assert.Equal(new VoiceAnnouncementSetting(true, 50), UserSettings.Default.EffectiveVoiceAnnouncement);
        var settings = UserSettings.Default with { VoiceAnnouncement = new(false, 17) };
        var copy = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        copy.Validate();
        Assert.Equal(new VoiceAnnouncementSetting(false, 17), copy.EffectiveVoiceAnnouncement);
        Assert.Throws<ArgumentException>(() => (settings with { VoiceAnnouncement = new(true, 101) }).Validate());
    }

    [Theory]
    [InlineData(RF4Overlay.Core.Audio.SoundCue.Alarm)]
    [InlineData(RF4Overlay.Core.Audio.SoundCue.Sound8)]
    [InlineData(RF4Overlay.Core.Audio.SoundCue.Sound0)]
    [InlineData(RF4Overlay.Core.Audio.SoundCue.Sound9)]
    [InlineData(RF4Overlay.Core.Audio.SoundCue.TradeReceived)]
    public void AlarmSelectionAndVolumeSurviveRestart(RF4Overlay.Core.Audio.SoundCue cue)
    {
        var settings = UserSettings.Default with { BiteAlarm = new(cue, 0.25f) };
        var copy = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        copy.Validate();
        Assert.Equal(settings.EffectiveBiteAlarm, copy.EffectiveBiteAlarm);
        Assert.Throws<ArgumentException>(() => (settings with { BiteAlarm = new(cue, float.NaN) }).Validate());
        Assert.Throws<ArgumentException>(() => (settings with { BiteAlarm = new(RF4Overlay.Core.Audio.SoundCue.Tick) }).Validate());
    }

    [Fact]
    public void SettingsRoundTripPreservesRepeatedKeysModifiersAndTiming()
    {
        var settings = new UserSettings(80, 0.3f,
            [new(FeatureId.Metronome, [new(97, KeyModifiers.Control), new(97, KeyModifiers.Control)], 600)], 0.75,
            new(AutomationInput.Keyboard((byte)'K'), 2.5, 1.2));
        var copy = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        copy.Validate();
        Assert.Equal(80, copy.Bpm);
        Assert.Equal(0.75, copy.EffectivePeriodSeconds);
        var binding = Assert.Single(copy.Bindings());
        Assert.Equal(TimeSpan.FromMilliseconds(600), binding.MaximumGap);
        Assert.Equal(2, binding.Sequence.Count);
        Assert.All(binding.Sequence, key => Assert.Equal(KeyModifiers.Control, key.Modifiers));
        Assert.Equal(AutomationInput.Keyboard((byte)'K'), copy.EffectiveAutoPilking.Input);
        Assert.Equal(2.5, copy.EffectiveAutoPilking.HoldSeconds);
        Assert.Equal(1.2, copy.EffectiveAutoPilking.ReleaseSeconds);
    }

    [Fact]
    public void InvalidSettingsFailBeforeReplacingWorkingBindings()
    {
        UserSettings.Default.Validate();
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with { Bpm = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with { PeriodSeconds = 0.1 }).Validate());
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with { Volume = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with
        {
            AutoPilking = new(AutomationInput.Keyboard(0x10), 3, 3)
        }).Validate());
        Assert.Throws<ArgumentException>(() => (UserSettings.Default with
        {
            AutoPilking = new(AutomationInput.MouseRight, 0, 3)
        }).Validate());
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
        Assert.Null(legacy.PeriodSeconds); Assert.Equal(new BiteAlarmSetting(), legacy.EffectiveBiteAlarm);
        Assert.Equal(new VoiceAnnouncementSetting(true, 50), legacy.EffectiveVoiceAnnouncement);
        Assert.Equal(0.4, legacy.EffectivePeriodSeconds, 10);
        Assert.Equal(AutoPilkingSetting.Default, legacy.EffectiveAutoPilking);
    }

    [Fact]
    public void ExistingDefaultVoiceVolumeMigratesFromThirtyThreeToFifty()
    {
        var legacy = JsonSerializer.Deserialize<UserSettings>("""{"Bpm":120,"Volume":0.5,"Hotkeys":[],"VoiceAnnouncement":{"Enabled":true,"Volume":33}}""")!;
        Assert.Equal(new VoiceAnnouncementSetting(true, 50), legacy.EffectiveVoiceAnnouncement);

        var current = legacy with
        {
            VoiceAnnouncement = legacy.EffectiveVoiceAnnouncement,
            SettingsFormatVersion = UserSettings.CurrentSettingsFormatVersion
        };
        var copy = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(current))!;
        Assert.Equal(new VoiceAnnouncementSetting(true, 50), copy.EffectiveVoiceAnnouncement);
    }

    [Fact]
    public void ExistingDefaultAutoPilkingCycleMigratesToOneAndThreePointFiveSeconds()
    {
        var legacy = JsonSerializer.Deserialize<UserSettings>("""{"Bpm":120,"Volume":0.5,"Hotkeys":[],"AutoPilking":{"Input":{"Kind":0,"VirtualKey":0},"HoldSeconds":3,"ReleaseSeconds":3},"SettingsFormatVersion":1}""")!;
        Assert.Equal(AutoPilkingSetting.Default, legacy.EffectiveAutoPilking);
        Assert.Equal(1, legacy.EffectiveAutoPilking.HoldSeconds);
        Assert.Equal(3.5, legacy.EffectiveAutoPilking.ReleaseSeconds);
    }

    [Fact]
    public void MissingPictureInPictureHotkeyGetsDefaultWithoutReplacingExistingBindings()
    {
        var existing = new HotkeySetting(FeatureId.Metronome, [new((byte)'M')], 700);
        var migrated = new UserSettings(120, 0.5f, [existing]).WithMissingDefaultHotkeys();

        migrated.Validate();
        Assert.Equal(existing, migrated.Hotkeys.Single(h => h.Feature == FeatureId.Metronome));
        var pip = migrated.Hotkeys.Single(h => h.Feature == FeatureId.PictureInPicture);
        Assert.Equal(0x7A, Assert.Single(pip.Keys).VirtualKey);
        Assert.Equal(KeyModifiers.Control, pip.Keys[0].Modifiers);
    }
}
