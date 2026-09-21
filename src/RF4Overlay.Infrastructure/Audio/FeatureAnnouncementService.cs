using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Settings;

namespace RF4Overlay.Infrastructure.Audio;

public sealed class FeatureAnnouncementService : IFeatureAnnouncement, IDisposable
{
    private readonly BlockingCollection<string> _messages = new();
    private readonly VoiceAnnouncementSettings _settings;
    private readonly Thread _worker;
    private int _disposed;

    public FeatureAnnouncementService(VoiceAnnouncementSettings? settings = null)
    {
        _settings = settings ?? new();
        _worker = new Thread(Run) { IsBackground = true, Name = "Feature announcements" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public void Announce(FeatureId feature, bool running)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_settings.Current.Enabled) return;
        try { _messages.Add(MessageFor(feature, running)); }
        catch (InvalidOperationException) { }
    }

    public static string MessageFor(FeatureId feature, bool running)
    {
        var name = feature switch
        {
            FeatureId.BiteAlarm => "the bite alarm",
            FeatureId.Metronome => "the metronome",
            FeatureId.AutoPilking => "auto pilking",
            FeatureId.PictureInPicture => "picture in picture",
            FeatureId.LeftClickHold => "left-click hold",
            _ => throw new ArgumentOutOfRangeException(nameof(feature))
        };
        return $"{(running ? "Starting" : "Stopping")} {name}.";
    }

    private void Run()
    {
        object? voice = null;
        object? englishVoices = null;
        object? englishVoice = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice")
                ?? throw new InvalidOperationException("Windows speech synthesis is unavailable.");
            voice = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Windows speech synthesis could not be started.");
            englishVoices = ((dynamic)voice).GetVoices("Language=409", "");
            if (((dynamic)englishVoices).Count > 0)
            {
                englishVoice = ((dynamic)englishVoices).Item(0);
                ((dynamic)voice).Voice = englishVoice;
            }
            foreach (var message in _messages.GetConsumingEnumerable())
            {
                var settings = _settings.Current;
                if (!settings.Enabled) continue;
                ((dynamic)voice).Volume = ToOutputVolume(settings.Volume);
                ((dynamic)voice).Speak(message, 0);
            }
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        finally
        {
            if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice);
            if (englishVoice is not null && Marshal.IsComObject(englishVoice)) Marshal.FinalReleaseComObject(englishVoice);
            if (englishVoices is not null && Marshal.IsComObject(englishVoices)) Marshal.FinalReleaseComObject(englishVoices);
        }
    }

    public static int ToOutputVolume(int displayedVolume)
    {
        var volume = Math.Clamp(displayedVolume, 0, 100);
        return volume <= 50
            ? (int)Math.Round(volume * 33d / 50d)
            : (int)Math.Round(33 + (volume - 50) * 67d / 50d);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _messages.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(5));
        _messages.Dispose();
    }
}
