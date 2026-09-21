using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RF4Overlay.Core.Capture;
using RF4Overlay.Features.BiteAlarm;

namespace RF4Overlay.WindowsTests;

public sealed class FishCaughtDetectorTests
{
    private static readonly string[] NegativeSamples =
        ["negative_01.png", "negative_02.png", "negative_03.png", "negative_04.png", "negative_boat_20260921.png"];

    [Fact]
    public void SuppliedPositiveMatchesAndAllFourNegativesDoNot()
    {
        var detector = new FishCaughtIconDetector();
        var positive = detector.Analyze(Load("positive_fish_caught.png"));

        Assert.True(positive.IsMatch,
            $"Positive evidence: ring={positive.AnnulusCoverage:F3}, quadrant={positive.MinimumQuadrantCoverage:F3}, inner={positive.InnerCoverage:F3}, outer={positive.OuterWhiteCoverage:F3}");
        foreach (var sample in NegativeSamples)
        {
            var evidence = detector.Analyze(Load(sample));
            Assert.False(evidence.IsMatch,
                $"{sample}: ring={evidence.AnnulusCoverage:F3}, quadrant={evidence.MinimumQuadrantCoverage:F3}, inner={evidence.InnerCoverage:F3}, outer={evidence.OuterWhiteCoverage:F3}");
        }
    }

    [Fact]
    public void WhiteFloodInsideRoiIsRejected()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 1000; y < 1046; y++)
        for (var x = 527; x < 574; x++)
        {
            var offset = y * stride + x * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = 255;
        }

        var evidence = new FishCaughtIconDetector().Analyze(new(width, height, stride, pixels));

        Assert.False(evidence.IsMatch);
        Assert.True(evidence.OuterWhiteCoverage > 0.35);
    }

    [Theory]
    [InlineData(2d / 3d)]
    [InlineData(4d / 3d)]
    public void NormalizedRoiTracksScaledFrames(double scale)
    {
        var detector = new FishCaughtIconDetector();
        Assert.True(detector.IsFishCaught(Load("positive_fish_caught.png", scale)));
        Assert.False(detector.IsFishCaught(Load("negative_03.png", scale)));
        Assert.True(detector.IsFishCaught(Load("positive_water_20260921.png", scale)));
        Assert.False(detector.IsFishCaught(Load("negative_boat_20260921.png", scale)));
    }

    [Fact]
    public void NewWaterPositiveHasCircularEdgesButBoatTextDoesNot()
    {
        var detector = new FishCaughtIconDetector();
        var positive = detector.Analyze(Load("positive_water_20260921.png"));
        var boat = detector.Analyze(Load("negative_boat_20260921.png"));
        Assert.True(positive.IsMatch, positive.ToString());
        Assert.True(positive.RingEdgeCoverage >= 0.75, positive.ToString());
        Assert.True(boat.RingEdgeCoverage < 0.75, boat.ToString());
        Assert.False(boat.IsMatch);
    }

    [Fact]
    public async Task BoatScreenshotAtOriginalSizeNeverAccumulatesAnAlarm()
    {
        // The supplied screenshot includes a capture border and is 1919x1079, not 1920x1080.
        var frame = Load("negative_boat_20260921.png", width: 1919, height: 1079);
        var detector = new FishCaughtIconDetector();
        for (var index = 0; index < 30; index++)
            Assert.Equal(BiteObservation.Absent, await detector.DetectAsync(frame, CancellationToken.None));
    }

    [Fact]
    public void BrightSolidDiskCannotImpersonateRingAndFish()
    {
        const int width = 1920, height = 1080, stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 1001; y < 1045; y++)
        for (var x = 528; x < 572; x++)
        {
            var dx = x + 0.5 - width * FishCaughtIconDetector.IconCenterX;
            var dy = y + 0.5 - height * FishCaughtIconDetector.IconCenterY;
            if (dx * dx + dy * dy > 18 * 18) continue;
            var offset = y * stride + x * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 200;
            pixels[offset + 3] = 255;
        }
        var evidence = new FishCaughtIconDetector().Analyze(new(width, height, stride, pixels));
        Assert.True(evidence.AnnulusCoverage >= 0.3 && evidence.InnerCoverage >= 0.25);
        Assert.True(evidence.OuterWhiteCoverage <= 0.35);
        Assert.False(evidence.IsMatch);
        Assert.Equal(0, evidence.RingEdgeCoverage);
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

    private static CapturedFrame Load(string name, double scale = 1, int width = 1920, int height = 1080)
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

        const int left = 528;
        const int top = 1001;
        var stride = width * 4;
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
