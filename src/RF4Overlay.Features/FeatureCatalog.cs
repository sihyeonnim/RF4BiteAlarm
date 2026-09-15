using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;
using RF4Overlay.Features.BiteAlarm;
using RF4Overlay.Features.Metronome;
using RF4Overlay.Features.AutoPilking;

namespace RF4Overlay.Features;

public static class FeatureCatalog
{
    public static IReadOnlyList<IFeature> Create(IAudioService audio, MetronomeSettings settings) =>
        [new BiteAlarmFeature(), new MetronomeFeature(audio, settings), new AutoPilkingFeature()];
}
