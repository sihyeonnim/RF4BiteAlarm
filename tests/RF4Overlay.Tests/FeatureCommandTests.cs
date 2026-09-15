using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Features;

namespace RF4Overlay.Tests;

public sealed class FeatureCommandTests
{
    [Theory]
    [InlineData(FeatureAction.Start)]
    [InlineData(FeatureAction.Stop)]
    [InlineData(FeatureAction.Toggle)]
    public void DispatchRoutesOnlyToSelectedFeature(FeatureAction action)
    {
        var selected = new RecordingFeature(FeatureId.BiteAlarm);
        var other = new RecordingFeature(FeatureId.Metronome);
        var dispatcher = new FeatureCommandDispatcher([selected, other]);
        Assert.True(dispatcher.Execute(new(FeatureId.BiteAlarm, action)).Succeeded);
        Assert.Equal(action, Assert.Single(selected.Received));
        Assert.Empty(other.Received);
    }

    [Fact]
    public void MissingFeatureReturnsFailure() => Assert.False(
        new FeatureCommandDispatcher([]).Execute(new(FeatureId.BiteAlarm, FeatureAction.Start)).Succeeded);

    [Fact]
    public void DuplicateFeatureIdsAreRejected() => Assert.Throws<ArgumentException>(() =>
        new FeatureCommandDispatcher([new RecordingFeature(FeatureId.BiteAlarm), new RecordingFeature(FeatureId.BiteAlarm)]));

    [Fact]
    public void PlannedFeaturesCannotPretendToStart()
    {
        var features = FeatureCatalog.Create();
        Assert.Equal(Enum.GetValues<FeatureId>().Length, features.Count);
        Assert.All(features, feature =>
        {
            Assert.Equal(FeatureState.Unavailable, feature.Status.State);
            foreach (var action in Enum.GetValues<FeatureAction>())
                Assert.False(feature.Execute(action).Succeeded);
        });
    }

    [Fact]
    public void SequencePreservesRepeatedKeysAndCopiesInput()
    {
        KeyStroke[] keys = [new(0x61), new(0x61), new(0x61)]; // NumPad1, representation only.
        var binding = new HotkeyBinding(keys, TimeSpan.FromMilliseconds(400), new(FeatureId.BiteAlarm, FeatureAction.Toggle));
        keys[0] = new(0x62);
        Assert.Equal(3, binding.Sequence.Count);
        Assert.All(binding.Sequence, key => Assert.Equal((byte)0x61, key.VirtualKey));
    }

    [Fact]
    public void InvalidSequencesAreRejected()
    {
        var command = new FeatureCommand(FeatureId.BiteAlarm, FeatureAction.Toggle);
        Assert.Throws<ArgumentException>(() => new HotkeyBinding([], TimeSpan.FromSeconds(1), command));
        Assert.Throws<ArgumentException>(() => new HotkeyBinding([new(0)], TimeSpan.FromSeconds(1), command));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyBinding([new(0x61)], TimeSpan.Zero, command));
    }

    private sealed class RecordingFeature(FeatureId id) : IFeature
    {
        public FeatureStatus Status { get; } = new(id, id.ToString(), FeatureState.Stopped, "Test");
        public List<FeatureAction> Received { get; } = [];
        public FeatureCommandResult Execute(FeatureAction action)
        {
            Received.Add(action);
            return new(true, "OK");
        }
    }
}
