using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RF4Overlay.Core.Audio;

namespace RF4Overlay.Infrastructure.Audio;

public sealed class AudioService : IAudioService
{
    public IAudioVoice CreateVoice() => new Voice();
    private sealed class Voice : IAudioVoice
    {
        private readonly WaveOutEvent _output = new() { DesiredLatency = 60, NumberOfBuffers = 3 };
        private readonly ToneProvider _tone = new();
        private Exception? _error;
        public Voice()
        {
            try
            {
                _output.PlaybackStopped += (_, e) => { if (e.Exception is not null) Volatile.Write(ref _error, e.Exception); };
                _output.Init(_tone);
                _output.Play();
            }
            catch { _output.Dispose(); throw; }
        }
        public void Play(SoundCue cue, float volume)
        {
            if (Volatile.Read(ref _error) is { } error) throw new InvalidOperationException("오디오 장치를 사용할 수 없습니다.", error);
            _tone.Trigger(cue, volume);
            _output.Play();
        }
        public void Stop() { _tone.Clear(); _output.Stop(); }
        public void Dispose() { _tone.Clear(); _output.Dispose(); }
    }
    private sealed class ToneProvider : ISampleProvider
    {
        private readonly object _sync = new();
        private int _position, _length;
        private float _volume;
        private double _frequency;
        private float[]? _clip;
        private static readonly Lazy<IReadOnlyDictionary<SoundCue, float[]>> Clips = new(LoadClips);
        private static IReadOnlyDictionary<SoundCue, float[]> LoadClips()
        {
            var result = new Dictionary<SoundCue, float[]>();
            foreach (var (cue, file) in new[] { (SoundCue.Sound8, "sound8.wav"), (SoundCue.Sound0, "sound0.wav"),
                (SoundCue.Sound9, "sound9.wav"), (SoundCue.TradeReceived, "체결수신1.wav") })
            {
                using var stream = typeof(AudioService).Assembly.GetManifestResourceStream("RF4Overlay.Infrastructure.Audio.Sounds." + file)
                    ?? throw new InvalidOperationException("알람 소리 파일이 없습니다: " + file);
                using var reader = new WaveFileReader(stream);
                ISampleProvider samples = reader.ToSampleProvider();
                if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
                if (samples.WaveFormat.SampleRate != 44100) samples = new WdlResamplingSampleProvider(samples, 44100);
                var decoded = new List<float>();
                var buffer = new float[4096];
                int count;
                while ((count = samples.Read(buffer, 0, buffer.Length)) > 0) decoded.AddRange(buffer.AsSpan(0, count).ToArray());
                result.Add(cue, decoded.ToArray());
            }
            return result;
        }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public void Trigger(SoundCue cue, float volume)
        {
            if (!float.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
            lock (_sync)
            {
                // Do not interrupt a long alarm when the repeat timer fires.
                if (cue != SoundCue.Tick && _position < _length) return;
                _clip = cue is SoundCue.Tick or SoundCue.Alarm ? null : Clips.Value[cue];
                _position = 0; _length = _clip?.Length ?? (cue == SoundCue.Tick ? 2205 : 17640);
                _frequency = cue == SoundCue.Tick ? 1200 : 880; _volume = volume;
            }
        }
        public void Clear() { lock (_sync) _length = 0; }
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                for (var i = 0; i < count; i++)
                {
                    float sample = 0;
                    if (_position < _length)
                    {
                        if (_clip is not null) sample = _clip[_position++] * _volume;
                        else
                        {
                        var envelope = Math.Min(1d, _position / 100d) * (1d - (double)_position / _length);
                        sample = (float)(Math.Sin(2 * Math.PI * _frequency * _position++ / 44100) * envelope * _volume * 0.5);
                        }
                    }
                    buffer[offset + i] = sample;
                }
            }
            return count; // Silence between one-shot sounds, no implicit loop.
        }
    }
}
