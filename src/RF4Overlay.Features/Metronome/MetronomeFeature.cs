using System.Diagnostics;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;

namespace RF4Overlay.Features.Metronome;

public sealed class MetronomeSettings
{
    public const double MinimumPeriodSeconds = 0.2;
    public const double MaximumPeriodSeconds = 60;

    private double _periodSeconds = 0.5;
    private float _volume = 0.5f;
    public double Bpm
    {
        get => 60d / PeriodSeconds;
        set
        {
            if (!double.IsFinite(value) || value is < 1 or > 300)
                throw new ArgumentOutOfRangeException(nameof(value), "BPM은 1–300입니다.");
            PeriodSeconds = 60d / value;
        }
    }
    public double PeriodSeconds
    {
        get => Volatile.Read(ref _periodSeconds);
        set
        {
            if (!double.IsFinite(value) || value is < MinimumPeriodSeconds or > MaximumPeriodSeconds)
                throw new ArgumentOutOfRangeException(nameof(value), "주기는 0.2–60초입니다.");
            Volatile.Write(ref _periodSeconds, value);
        }
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
                target = BeatSchedule.Next(target, clock.Elapsed, TimeSpan.FromSeconds(settings.PeriodSeconds));
                TimeSpan remaining;
                while ((remaining = target - clock.Elapsed) > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { voice.Stop(); }
    }
}
