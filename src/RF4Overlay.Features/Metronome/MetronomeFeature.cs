using System.Diagnostics;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;

namespace RF4Overlay.Features.Metronome;

public sealed class MetronomeSettings
{
    private int _bpm = 120;
    private float _volume = 0.5f;
    public int Bpm
    {
        get => Volatile.Read(ref _bpm);
        set { if (value is < 20 or > 300) throw new ArgumentOutOfRangeException(nameof(value), "BPM은 20–300입니다."); Volatile.Write(ref _bpm, value); }
    }
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set { if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value)); Volatile.Write(ref _volume, value); }
    }
}

public static class BeatSchedule
{
    public static TimeSpan Next(TimeSpan previousTarget, TimeSpan now, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        var next = previousTarget + interval;
        if (next < now) next += TimeSpan.FromTicks(((now - next).Ticks / interval.Ticks + 1) * interval.Ticks);
        return next;
    }
}

public sealed class MetronomeFeature(IAudioService audio, MetronomeSettings settings) : IFeature
{
    public FeatureStatus InitialStatus { get; } = new(FeatureId.Metronome, "Metronome", FeatureState.Stopped, "박자에 맞춰 소리를 재생합니다.");
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var voice = audio.CreateVoice();
        var clock = Stopwatch.StartNew();
        var target = TimeSpan.Zero;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                voice.Play(SoundCue.Tick, settings.Volume);
                target = BeatSchedule.Next(target, clock.Elapsed, TimeSpan.FromSeconds(60d / settings.Bpm));
                TimeSpan remaining;
                while ((remaining = target - clock.Elapsed) > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { voice.Stop(); }
    }
}
