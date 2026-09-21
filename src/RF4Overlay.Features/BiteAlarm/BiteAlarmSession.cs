using System.Diagnostics;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Capture;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Features.BiteAlarm;

/// <summary>Detector-independent alarm loop; not enabled in the catalog until actual detector data exists.</summary>
public sealed class BiteAlarmSession(IAudioService audio, IUserInputSource input, BiteAlarmOptions options, BiteAlarmSettings? settings = null)
{
    private int _running;
    public async Task RunAsync(IAsyncEnumerable<CapturedFrame> frames, IBiteDetector detector, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("알람 세션이 이미 실행 중입니다.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var voice = audio.CreateVoice();
            var state = new BiteStateMachine(options);
            var clock = Stopwatch.StartNew();
            var sync = new object();
            var active = true;
            Exception? inputError = null;
            state.Start();
            void PlayAlarm()
            {
                var current = settings?.Current;
                voice.Play(current?.Sound ?? SoundCue.Alarm, current?.Volume ?? options.Volume);
            }
            void OnInput(object? sender, UserInput activity)
            {
                lock (sync)
                {
                    if (!active || linked.IsCancellationRequested) return;
                    try { state.Acknowledge(); }
                    catch (Exception error) { inputError = error; linked.Cancel(); }
                }
            }
            input.InputReceived += OnInput;
            async Task ObserveAsync()
            {
                try
                {
                    await foreach (var frame in frames.WithCancellation(linked.Token).ConfigureAwait(false))
                    {
                        var observation = await detector.DetectAsync(frame, linked.Token).ConfigureAwait(false);
                        lock (sync)
                        {
                            if (!active || linked.IsCancellationRequested) return;
                            if (state.Observe(observation, clock.Elapsed)) PlayAlarm();
                        }
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("캡처 스트림이 종료되었습니다.");
                }
                finally { await linked.CancelAsync().ConfigureAwait(false); }
            }
            async Task RepeatAsync()
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
                    while (await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false))
                    {
                        lock (sync)
                        {
                            if (active && !linked.IsCancellationRequested && state.Tick(clock.Elapsed))
                                PlayAlarm();
                        }
                    }
                }
                finally { await linked.CancelAsync().ConfigureAwait(false); }
            }
            try
            {
                await Task.WhenAll(ObserveAsync(), RepeatAsync()).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (inputError is not null)
            {
                throw new InvalidOperationException("입력 확인 중 오디오 정리에 실패했습니다.", inputError);
            }
            finally
            {
                input.InputReceived -= OnInput;
                lock (sync) { active = false; state.Stop(); voice.Stop(); }
            }
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}
