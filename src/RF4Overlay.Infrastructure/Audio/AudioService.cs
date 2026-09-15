using NAudio.Wave;
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
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public void Trigger(SoundCue cue, float volume)
        {
            if (!float.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
            lock (_sync)
            {
                _position = 0; _length = cue == SoundCue.Tick ? 2205 : 17640;
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
                        var envelope = Math.Min(1d, _position / 100d) * (1d - (double)_position / _length);
                        sample = (float)(Math.Sin(2 * Math.PI * _frequency * _position++ / 44100) * envelope * _volume * 0.5);
                    }
                    buffer[offset + i] = sample;
                }
            }
            return count; // Silence between one-shot sounds, no implicit loop.
        }
    }
}
