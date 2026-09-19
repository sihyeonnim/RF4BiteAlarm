using RF4Overlay.Core.Capture;
using RF4Overlay.Core.Features;

namespace RF4Overlay.Features.PictureInPicture;

public sealed class PictureInPictureFeature(
    ICaptureFrameSource frames,
    IPictureInPicturePresenter presenter) : IFeature
{
    public FeatureStatus InitialStatus { get; } = new(
        FeatureId.PictureInPicture,
        "RF4 PIP",
        FeatureState.Stopped,
        "RF4 화면을 항상 위에 표시합니다.");

    public Task RunAsync(CancellationToken cancellationToken) => presenter.ShowAsync(frames, cancellationToken);
}

public sealed class UnavailablePictureInPictureFeature() : PlannedFeature(
    FeatureId.PictureInPicture, "RF4 PIP", "화면 표시 서비스가 필요합니다.");
