using RF4Overlay.Core.Features;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Capture;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Features.BiteAlarm;

public sealed class BiteAlarmFeature(
    IAudioService audio,
    IUserInputSource input,
    ICaptureFrameSource frames,
    Func<IBiteDetector> detectorFactory,
    BiteAlarmOptions options) : IFeature
{
    public FeatureStatus InitialStatus { get; } = new(
        FeatureId.BiteAlarm,
        "Bite Alarm",
        FeatureState.Stopped,
        "물고기 포획 아이콘을 감지해 반복 알람을 재생합니다.");

    public Task RunAsync(CancellationToken cancellationToken) =>
        new BiteAlarmSession(audio, input, options)
            .RunAsync(frames.ReadFramesAsync(cancellationToken), detectorFactory(), cancellationToken);
}

public sealed class UnavailableBiteAlarmFeature() : PlannedFeature(
    FeatureId.BiteAlarm, "Bite Alarm", "실제 캡처 소스가 필요합니다.");
