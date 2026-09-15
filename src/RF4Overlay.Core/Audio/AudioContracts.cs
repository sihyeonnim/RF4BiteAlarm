namespace RF4Overlay.Core.Audio;
public enum SoundCue { Tick, Alarm }
public interface IAudioVoice : IDisposable
{
    void Play(SoundCue cue, float volume);
    void Stop();
}
public interface IAudioService
{
    IAudioVoice CreateVoice();
}
