using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RF4Overlay.Core.Capture;
using RF4Overlay.Features.BiteAlarm;

namespace RF4Overlay.WindowsTests;

public sealed class FishCaughtDetectorTests
{
    private static readonly string[] NegativeSamples =
        ["negative_01.png", "negative_02.png", "negative_03.png", "negative_04.png"];

    [Fact]
    public void SuppliedPositiveMatchesAndAllFourNegativesDoNot()
    {
        var detector = new FishCaughtIconDetector();
        var positive = detector.Analyze(Load("positive_fish_caught.png"));

        Assert.True(positive.IsMatch,
            $"Positive evidence: ring={positive.AnnulusCoverage:F3}, quadrant={positive.MinimumQuadrantCoverage:F3}, inner={positive.InnerCoverage:F3}");
        foreach (var sample in NegativeSamples)
        {
            var evidence = detector.Analyze(Load(sample));
            Assert.False(evidence.IsMatch,
                $"{sample}: ring={evidence.AnnulusCoverage:F3}, quadrant={evidence.MinimumQuadrantCoverage:F3}, inner={evidence.InnerCoverage:F3}");
        }
    }

    [Theory]
    [InlineData(2d / 3d)]
    [InlineData(4d / 3d)]
    public void NormalizedRoiTracksScaledFrames(double scale)
    {
        var detector = new FishCaughtIconDetector();
        Assert.True(detector.IsFishCaught(Load("positive_fish_caught.png", scale)));
        Assert.False(detector.IsFishCaught(Load("negative_03.png", scale)));
    }

    [Fact]
    public async Task ThreeConsecutivePositiveFramesAreRequiredAndNegativeResetsProgress()
    {
        var detector = new FishCaughtIconDetector(new(ConsecutivePresentFrames: 3));
        var positive = Load("positive_fish_caught.png");
        var negative = Load("negative_03.png");

        Assert.Equal(BiteObservation.Absent, await detector.DetectAsync(positive, CancellationToken.None));
        Assert.Equal(BiteObservation.Absent, await detector.DetectAsync(positive, CancellationToken.None));
        Assert.Equal(BiteObservation.Absent, await detector.DetectAsync(negative, CancellationToken.None));
        Assert.Equal(BiteObservation.Absent, await detector.DetectAsync(positive, CancellationToken.None));
        Assert.Equal(BiteObservation.Absent, await detector.DetectAsync(positive, CancellationToken.None));
        Assert.Equal(BiteObservation.Present, await detector.DetectAsync(positive, CancellationToken.None));
    }

    private static CapturedFrame Load(string name, double scale = 1)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "FishCaught", name);
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var patch = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        Assert.Equal(44, patch.PixelWidth);
        Assert.Equal(44, patch.PixelHeight);
        var patchStride = patch.PixelWidth * 4;
        var patchPixels = new byte[patchStride * patch.PixelHeight];
        patch.CopyPixels(patchPixels, patchStride, 0);

        const int width = 1920;
        const int height = 1080;
        const int left = 528;
        const int top = 1001;
        const int stride = width * 4;
        var canvas = new byte[stride * height];
        for (var index = 3; index < canvas.Length; index += 4) canvas[index] = 255;
        for (var row = 0; row < patch.PixelHeight; row++)
            Buffer.BlockCopy(patchPixels, row * patchStride, canvas, (top + row) * stride + left * 4, patchStride);

        if (Math.Abs(scale - 1) <= 0.0001)
            return new(width, height, stride, canvas);

        BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
            null, canvas, stride);
        if (Math.Abs(scale - 1) > 0.0001)
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var scaledStride = source.PixelWidth * 4;
        var pixels = new byte[scaledStride * source.PixelHeight];
        source.CopyPixels(pixels, scaledStride, 0);
        return new(source.PixelWidth, source.PixelHeight, scaledStride, pixels);
    }
}
