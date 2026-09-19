using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Capture;
using RF4Overlay.Core.Input;
using RF4Overlay.Features.BiteAlarm;

namespace RF4Overlay.Tests;

public sealed class BiteAlarmTests
{
    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);
    private static BiteStateMachine Machine() => new(new(Seconds(3), Seconds(0.5)));

    [Fact]
    public void ImmediateRepeatAcknowledgeAndPersistentUiCannotRetrigger()
    {
        var state = Machine();
        Assert.False(state.Observe(BiteObservation.Present, Seconds(0)));
        state.Start();
        Assert.True(state.Observe(BiteObservation.Present, Seconds(0)));
        Assert.False(state.Observe(BiteObservation.Present, Seconds(1)));
        Assert.False(state.Tick(Seconds(2.99)));
        Assert.True(state.Tick(Seconds(3)));
        Assert.True(state.Tick(Seconds(100)));
        Assert.False(state.Tick(Seconds(100)));
        Assert.True(state.Acknowledge());
        Assert.False(state.Tick(Seconds(200)));
        Assert.False(state.Observe(BiteObservation.Present, Seconds(201)));
        Assert.Equal(BiteState.WaitingForDisappearance, state.State);
    }

    [Fact]
    public void RearmRequiresStableAbsenceAndCaptureFailureResetsEvidence()
    {
        var state = Machine(); state.Start(); state.Observe(BiteObservation.Present, Seconds(0)); state.Acknowledge();
        state.Observe(BiteObservation.Absent, Seconds(1));
        state.Observe(BiteObservation.CaptureFailed, Seconds(1.4));
        state.Observe(BiteObservation.Absent, Seconds(2));
        Assert.Equal(BiteState.WaitingForDisappearance, state.State);
        state.Observe(BiteObservation.Present, Seconds(2.3));
        state.Observe(BiteObservation.Absent, Seconds(3));
        state.Observe(BiteObservation.Absent, Seconds(3.49));
        Assert.Equal(BiteState.WaitingForDisappearance, state.State);
        state.Observe(BiteObservation.Absent, Seconds(3.5));
        Assert.Equal(BiteState.Monitoring, state.State);
        Assert.True(state.Observe(BiteObservation.Present, Seconds(4)));
        state.Stop();
        Assert.False(state.Tick(Seconds(10)));
        Assert.False(state.Acknowledge());
    }

    [Fact]
    public void UiDisappearanceAloneDoesNotAcknowledgeAnAlarm()
    {
        var state = Machine(); state.Start(); state.Observe(BiteObservation.Present, Seconds(0));
        state.Observe(BiteObservation.Absent, Seconds(1));
        state.Observe(BiteObservation.Absent, Seconds(5));
        Assert.True(state.Tick(Seconds(5)));
        state.Observe(BiteObservation.CaptureFailed, Seconds(6));
        Assert.True(state.Tick(Seconds(8)));
    }

    [Fact]
    public async Task SessionRepeatsThenAcknowledgesAndCancellationUnsubscribes()
    {
        var source = new FakeInput();
        var audio = new AlarmAudio();
        var frames = Channel.CreateUnbounded<CapturedFrame>();
        using var cts = new CancellationTokenSource();
        var session = new BiteAlarmSession(audio, source, new(Seconds(0.04), Seconds(0.05)));
        var task = session.RunAsync(frames.Reader.ReadAllAsync(cts.Token), new PresentDetector(), cts.Token);
        frames.Writer.TryWrite(new(1, 1, 4, new byte[4]));
        await audio.Twice.Task.WaitAsync(Seconds(5));
        source.Acknowledge();
        var count = audio.Count;
        frames.Writer.TryWrite(new(1, 1, 4, new byte[4]));
        await Task.Delay(120);
        Assert.Equal(count, audio.Count);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, source.Subscribers);
        Assert.True(audio.Disposed);
        source.Acknowledge();
        Assert.Equal(count, audio.Count);
    }

    [Fact]
    public async Task CaptureFailureTerminatesSessionAndReleasesAudio()
    {
        var audio = new AlarmAudio();
        var input = new FakeInput();
        var session = new BiteAlarmSession(audio, input, new(Seconds(1), Seconds(0.5)));
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(FailingFrames(), new PresentDetector(), CancellationToken.None));
        Assert.True(audio.Disposed); Assert.Equal(0, input.Subscribers);
    }

    [Fact]
    public async Task InvalidDetectorFrameIsReportedAsCaptureFailure()
    {
        var detector = new FishCaughtIconDetector();
        var invalid = new CapturedFrame(1, 1, 4, new byte[4]);
        Assert.Equal(BiteObservation.CaptureFailed,
            await detector.DetectAsync(invalid, CancellationToken.None));
    }

    private static async IAsyncEnumerable<CapturedFrame> FailingFrames([EnumeratorCancellation] CancellationToken token = default)
    {
        await Task.Yield(); token.ThrowIfCancellationRequested();
        yield return new(1, 1, 4, new byte[4]);
        throw new IOException("Device lost");
    }

    private sealed class PresentDetector : IBiteDetector
    {
        public ValueTask<BiteObservation> DetectAsync(CapturedFrame frame, CancellationToken token) => ValueTask.FromResult(BiteObservation.Present);
    }
    private sealed class FakeInput : IUserInputSource
    {
        private EventHandler<UserInput>? _input;
        public int Subscribers;
        public event EventHandler<UserInput>? InputReceived
        {
            add { _input += value; Subscribers++; }
            remove { _input -= value; Subscribers--; }
        }
        public void Acknowledge() => _input?.Invoke(this, new(DateTimeOffset.UtcNow));
    }
    private sealed class AlarmAudio : IAudioService, IAudioVoice
    {
        public int Count; public bool Disposed;
        public TaskCompletionSource Twice { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IAudioVoice CreateVoice() => this;
        public void Play(SoundCue cue, float volume) { if (Interlocked.Increment(ref Count) >= 2) Twice.TrySetResult(); }
        public void Stop() { }
        public void Dispose() => Disposed = true;
    }
}
