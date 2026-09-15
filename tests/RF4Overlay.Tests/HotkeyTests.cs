using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Tests;

public sealed class HotkeyTests
{
    private static HotkeyBinding Binding(byte[] keys, KeyModifiers modifiers = KeyModifiers.None) =>
        new(keys.Select(k => new KeyStroke(k, modifiers)), TimeSpan.FromMilliseconds(400), new(FeatureId.Metronome, FeatureAction.Toggle));
    private static FeatureCommand? Press(HotkeyMatcher matcher, byte key, int ms)
    {
        var result = matcher.Process(new(key, true, false, TimeSpan.FromMilliseconds(ms)));
        matcher.Process(new(key, false, false, TimeSpan.FromMilliseconds(ms + 1)));
        return result;
    }
    [Fact]
    public void RepeatedKeyRequiresReleasesAndHonorsTimeout()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([Binding([0x61, 0x61, 0x61])]);
        Assert.Null(matcher.Process(new(0x61, true, false, TimeSpan.Zero)));
        Assert.Null(matcher.Process(new(0x61, true, false, TimeSpan.FromMilliseconds(10))));
        matcher.Process(new(0x61, false, false, TimeSpan.FromMilliseconds(20)));
        Assert.Null(Press(matcher, 0x61, 100));
        Assert.NotNull(Press(matcher, 0x61, 200));
        Assert.Null(Press(matcher, 0x61, 300));
        Assert.Null(Press(matcher, 0x61, 800));
        Assert.Null(Press(matcher, 0x61, 900));
        Assert.NotNull(Press(matcher, 0x61, 1000));
    }
    [Fact]
    public void ModifiersAreExactAndInjectedInputCannotTriggerOrReleasePhysicalKey()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([Binding([0x77], KeyModifiers.Control | KeyModifiers.Shift)]);
        Assert.Null(Press(matcher, 0x77, 0));
        matcher.Process(new(0xA2, true, false, TimeSpan.Zero));
        matcher.Process(new(0xA0, true, false, TimeSpan.Zero));
        Assert.Null(matcher.Process(new(0x77, true, true, TimeSpan.Zero)));
        matcher.Process(new(0xA2, false, true, TimeSpan.Zero));
        Assert.NotNull(Press(matcher, 0x77, 20));
        matcher.Process(new(0xA0, false, false, TimeSpan.Zero));
        Assert.Null(Press(matcher, 0x77, 30));
        Assert.False(InputPolicy.IsPhysical(true));
    }
    [Fact]
    public void PrefixAndDuplicateConflictsRejectedWithoutLosingExistingBinding()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([Binding([0x77])]);
        Assert.Throws<ArgumentException>(() => matcher.SetBindings([Binding([65,65]), Binding([65,65,65])]));
        Assert.Throws<ArgumentException>(() => matcher.SetBindings([Binding([65]), Binding([65])]));
        Assert.NotNull(Press(matcher, 0x77, 0));
    }
    [Fact]
    public void MixedSequenceSupportsOverlappingRestart()
    {
        var matcher = new HotkeyMatcher();
        matcher.SetBindings([Binding([81,81,69])]);
        Assert.Null(Press(matcher, 81, 0));
        Assert.Null(Press(matcher, 81, 10));
        Assert.Null(Press(matcher, 81, 20));
        Assert.NotNull(Press(matcher, 69, 30));
        matcher.Reset();
        Assert.Null(Press(matcher, 69, 40));
    }
    [Fact]
    public void BindingCopiesAndValidatesKeys()
    {
        KeyStroke[] keys = [new(0x61)];
        var command = new FeatureCommand(FeatureId.Metronome, FeatureAction.Toggle);
        var binding = new HotkeyBinding(keys, TimeSpan.FromSeconds(1), command);
        keys[0] = new(0x62);
        Assert.Equal((byte)0x61, binding.Sequence[0].VirtualKey);
        Assert.Throws<ArgumentException>(() => new HotkeyBinding([], TimeSpan.FromSeconds(1), command));
        Assert.Throws<ArgumentException>(() => new HotkeyBinding([new(0)], TimeSpan.FromSeconds(1), command));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyBinding([new(65)], TimeSpan.Zero, command));
    }
}
