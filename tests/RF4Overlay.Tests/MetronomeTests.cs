using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;
using RF4Overlay.Features.Metronome;

namespace RF4Overlay.Tests;

public sealed class MetronomeTests
{
    [Fact]
    public void ScheduleAnchorsToTargetsAndSkipsMissedBeats()
    {
        var interval = TimeSpan.FromMilliseconds(500);
        var target = TimeSpan.Zero;
        for (var i = 0; i < 10000; i++)
            target = BeatSchedule.Next(target, target + TimeSpan.FromMilliseconds(7), interval);
        Assert.Equal(TimeSpan.FromSeconds(5000), target);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), BeatSchedule.Next(TimeSpan.Zero, TimeSpan.FromMilliseconds(1600), interval));
    }
    [Fact]
    public async Task StartStopRestartDisposesVoicesAndStopsTicks()
    {
        var audio = new FakeAudio();
        await using var runtime = new FeatureCommandDispatcher([new MetronomeFeature(audio, new() { Bpm = 300 })]);
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await audio.Played.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        var count = audio.Count;
        await Task.Delay(250);
        Assert.Equal(count, audio.Count);
        Assert.Equal(1, audio.Disposed);
        audio.Played = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Start));
        await audio.Played.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.ExecuteAsync(new(FeatureId.Metronome, FeatureAction.Stop));
        Assert.Equal(2, audio.Disposed);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void InvalidBpmRejected(double bpm) => Assert.Throws<ArgumentOutOfRangeException>(() => new MetronomeSettings { Bpm = bpm });

    [Fact]
    public void PeriodAndBpmStayInSyncWithoutLosingFractionalSeconds()
    {
        var settings = new MetronomeSettings { PeriodSeconds = 0.7 };
        Assert.Equal(0.7, settings.PeriodSeconds, 10);
        Assert.Equal(60d / 0.7, settings.Bpm, 10);

        settings.Bpm = 40;
        Assert.Equal(1.5, settings.PeriodSeconds, 10);
    }

    [Theory]
    [InlineData(0.19)]
    [InlineData(60.01)]
    public void InvalidPeriodRejected(double seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetronomeSettings { PeriodSeconds = seconds });

    internal sealed class FakeAudio : IAudioService
    {
        public int Count, Disposed;
        public TaskCompletionSource Played = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IAudioVoice CreateVoice() => new FakeVoice(this);
        private sealed class FakeVoice(FakeAudio owner) : IAudioVoice
        {
            public void Play(SoundCue cue, float volume) { Interlocked.Increment(ref owner.Count); owner.Played.TrySetResult(); }
            public void Stop() { }
            public void Dispose() => Interlocked.Increment(ref owner.Disposed);
        }
    }
}
