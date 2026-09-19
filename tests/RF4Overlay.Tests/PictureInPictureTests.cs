using RF4Overlay.Core.Capture;
using RF4Overlay.Features.PictureInPicture;

namespace RF4Overlay.Tests;

public sealed class PictureInPictureTests
{
    [Fact]
    public async Task FeatureDelegatesSharedFramesAndCancellationToPresenter()
    {
        var frames = new FakeFrames();
        var presenter = new FakePresenter();
        var feature = new PictureInPictureFeature(frames, presenter);
        using var cancellation = new CancellationTokenSource();

        var run = feature.RunAsync(cancellation.Token);
        Assert.Same(frames, await presenter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class FakeFrames : ICaptureFrameSource
    {
        public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class FakePresenter : IPictureInPicturePresenter
    {
        public TaskCompletionSource<ICaptureFrameSource> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ShowAsync(ICaptureFrameSource frames, CancellationToken cancellationToken)
        {
            Started.TrySetResult(frames);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
