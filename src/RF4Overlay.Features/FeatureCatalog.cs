using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;
using RF4Overlay.Features.BiteAlarm;
using RF4Overlay.Features.Metronome;
using RF4Overlay.Features.AutoPilking;
using RF4Overlay.Core.Capture;
using RF4Overlay.Core.Input;
using RF4Overlay.Features.PictureInPicture;
using RF4Overlay.Features.LeftClickHold;

namespace RF4Overlay.Features;

public static class FeatureCatalog
{
    public static IReadOnlyList<IFeature> Create(IAudioService audio, MetronomeSettings settings) =>
        [new UnavailableBiteAlarmFeature(), new MetronomeFeature(audio, settings), new UnavailableAutoPilkingFeature(),
            new UnavailablePictureInPictureFeature(), new UnavailableLeftClickHoldFeature()];

    public static IReadOnlyList<IFeature> Create(IAudioService audio, MetronomeSettings settings,
        IUserInputSource input, ICaptureFrameSource frames, IInputAutomation automation, AutoPilkingSettings autoSettings,
        IPictureInPicturePresenter pictureInPicture, IMouseInputSource mouseInput,
        ILeftButtonHoldAutomation leftButtonAutomation, LeftClickHoldSettings leftClickSettings) =>
        [new BiteAlarmFeature(audio, input, frames, () => new FishCaughtIconDetector(),
                new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.5))),
            new MetronomeFeature(audio, settings), new AutoPilkingFeature(automation, autoSettings),
            new PictureInPictureFeature(frames, pictureInPicture),
            new LeftClickHoldFeature(mouseInput, leftButtonAutomation, leftClickSettings, LeftClickHoldOptions.Default)];
}
