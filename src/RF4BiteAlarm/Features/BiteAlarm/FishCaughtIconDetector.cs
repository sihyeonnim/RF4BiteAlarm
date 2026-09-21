using RF4Overlay.Core.Capture;

namespace RF4Overlay.Features.BiteAlarm;

public sealed record FishCaughtIconDetectorOptions(
    int ConsecutivePresentFrames = 3,
    double MinimumAnnulusCoverage = 0.30,
    double MinimumQuadrantCoverage = 0.25,
    double MinimumInnerCoverage = 0.25,
    double MaximumOuterWhiteCoverage = 0.35,
    double MinimumRingEdgeCoverage = 0.75)
{
    public void Validate()
    {
        if (ConsecutivePresentFrames is < 1 or > 30 ||
            !ValidRatio(MinimumAnnulusCoverage) ||
            !ValidRatio(MinimumQuadrantCoverage) ||
            !ValidRatio(MinimumInnerCoverage) ||
            !ValidRatio(MaximumOuterWhiteCoverage) ||
            !ValidRatio(MinimumRingEdgeCoverage))
            throw new ArgumentException("물고기 포획 아이콘 감지 설정이 잘못되었습니다.");
    }

    private static bool ValidRatio(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}

public readonly record struct FishCaughtEvidence(
    double AnnulusCoverage,
    double MinimumQuadrantCoverage,
    double InnerCoverage,
    double OuterWhiteCoverage,
    bool IsMatch,
    double RingEdgeCoverage = 0);

/// <summary>
/// Detects the stable white ring and inner fish glyph only inside the supplied normalized RF4 UI region.
/// It never compares pixels outside that region.
/// </summary>
public sealed class FishCaughtIconDetector : IBiteDetector
{
    // Supplied from 1920x1080 RF4 samples. Geometry below is evaluated in this reference coordinate space.
    public const double RoiLeft = 0.2750;
    public const double RoiTop = 0.9269;
    public const double RoiWidth = 0.0229;
    public const double RoiHeight = 0.0407;
    public const double IconCenterX = 0.2865;
    public const double IconCenterY = 0.9481;

    private const double ReferenceWidth = 1920;
    private const double ReferenceHeight = 1080;
    private const double InnerRadius = 10;
    private const double AnnulusInnerRadius = 11;
    private const double AnnulusOuterRadius = 18;
    private const double OuterBackgroundInnerRadius = 20;
    private readonly FishCaughtIconDetectorOptions _options;
    private int _consecutivePresent;

    public FishCaughtIconDetector(FishCaughtIconDetectorOptions? options = null)
    {
        _options = options ?? new();
        _options.Validate();
    }

    public bool IsFishCaught(CapturedFrame frame) => Analyze(frame).IsMatch;

    public FishCaughtEvidence Analyze(CapturedFrame frame)
    {
        ValidateFrame(frame);
        var left = Math.Clamp((int)Math.Floor(frame.Width * RoiLeft), 0, frame.Width - 1);
        var top = Math.Clamp((int)Math.Floor(frame.Height * RoiTop), 0, frame.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(frame.Width * (RoiLeft + RoiWidth)), left + 1, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(frame.Height * (RoiTop + RoiHeight)), top + 1, frame.Height);
        var centerX = frame.Width * IconCenterX;
        var centerY = frame.Height * IconCenterY;
        var annulusTotal = 0;
        var annulusWhite = 0;
        var innerTotal = 0;
        var innerWhite = 0;
        var outerTotal = 0;
        var outerWhite = 0;
        Span<int> quadrantTotal = stackalloc int[4];
        Span<int> quadrantWhite = stackalloc int[4];
        var pixels = frame.Pixels.Span;

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var dx = (x + 0.5 - centerX) * ReferenceWidth / frame.Width;
                var dy = (y + 0.5 - centerY) * ReferenceHeight / frame.Height;
                var radius = Math.Sqrt(dx * dx + dy * dy);
                var white = IsNeutralWhite(pixels, y * frame.Stride + x * 4);
                if (radius <= InnerRadius)
                {
                    innerTotal++;
                    if (white) innerWhite++;
                }
                else if (radius >= AnnulusInnerRadius && radius <= AnnulusOuterRadius)
                {
                    var quadrant = (x + 0.5 >= centerX ? 1 : 0) + (y + 0.5 >= centerY ? 2 : 0);
                    annulusTotal++;
                    quadrantTotal[quadrant]++;
                    if (!white) continue;
                    annulusWhite++;
                    quadrantWhite[quadrant]++;
                }
                else if (radius >= OuterBackgroundInnerRadius)
                {
                    outerTotal++;
                    if (white) outerWhite++;
                }
            }
        }

        var annulusCoverage = Ratio(annulusWhite, annulusTotal);
        var innerCoverage = Ratio(innerWhite, innerTotal);
        var outerWhiteCoverage = Ratio(outerWhite, outerTotal);
        var minimumQuadrantCoverage = 1d;
        for (var index = 0; index < 4; index++)
            minimumQuadrantCoverage = Math.Min(minimumQuadrantCoverage,
                Ratio(quadrantWhite[index], quadrantTotal[index]));
        var ringEdgeCoverage = MeasureRingEdges(frame, centerX, centerY);
        var match = annulusCoverage >= _options.MinimumAnnulusCoverage &&
                    minimumQuadrantCoverage >= _options.MinimumQuadrantCoverage &&
                    innerCoverage >= _options.MinimumInnerCoverage &&
                    outerWhiteCoverage <= _options.MaximumOuterWhiteCoverage &&
                    ringEdgeCoverage >= _options.MinimumRingEdgeCoverage;
        return new(annulusCoverage, minimumQuadrantCoverage, innerCoverage, outerWhiteCoverage, match, ringEdgeCoverage);
    }

    // A genuine ring has a bright crest with darker pixels on BOTH sides. Text, wood and
    // water may fill the broad annulus but do not form this edge around the whole circle.
    // Relative contrast also preserves the dimmed icon in the supplied older screenshot.
    private static double MeasureRingEdges(CapturedFrame frame, double centerX, double centerY)
    {
        const int directions = 16;
        var matchingDirections = 0;
        for (var index = 0; index < directions; index++)
        {
            var angle = index * Math.Tau / directions;
            var dx = Math.Cos(angle) * frame.Width / ReferenceWidth;
            var dy = Math.Sin(angle) * frame.Height / ReferenceHeight;
            var crest = 0d;
            for (var radius = 12; radius <= 16; radius++)
                crest = Math.Max(crest, SampleNeutralBrightness(frame, centerX + radius * dx, centerY + radius * dy));
            var inside = (SampleNeutralBrightness(frame, centerX + 9 * dx, centerY + 9 * dy) +
                          SampleNeutralBrightness(frame, centerX + 10 * dx, centerY + 10 * dy)) / 2;
            var outside = (SampleNeutralBrightness(frame, centerX + 19 * dx, centerY + 19 * dy) +
                           SampleNeutralBrightness(frame, centerX + 20 * dx, centerY + 20 * dy)) / 2;
            if (crest >= 120 && crest - Math.Max(inside, outside) >= 25) matchingDirections++;
        }
        return (double)matchingDirections / directions;
    }

    private static double SampleNeutralBrightness(CapturedFrame frame, double x, double y)
    {
        var column = Math.Clamp((int)Math.Floor(x), 0, frame.Width - 1);
        var row = Math.Clamp((int)Math.Floor(y), 0, frame.Height - 1);
        var offset = row * frame.Stride + column * 4;
        var pixels = frame.Pixels.Span;
        return Math.Min(pixels[offset], Math.Min(pixels[offset + 1], pixels[offset + 2]));
    }

    public ValueTask<BiteObservation> DetectAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FishCaughtEvidence evidence;
        try { evidence = Analyze(frame); }
        catch (ArgumentException)
        {
            _consecutivePresent = 0;
            return ValueTask.FromResult(BiteObservation.CaptureFailed);
        }

        if (!evidence.IsMatch)
        {
            _consecutivePresent = 0;
            return ValueTask.FromResult(BiteObservation.Absent);
        }

        _consecutivePresent = Math.Min(_options.ConsecutivePresentFrames, _consecutivePresent + 1);
        return ValueTask.FromResult(_consecutivePresent >= _options.ConsecutivePresentFrames
            ? BiteObservation.Present
            : BiteObservation.Absent);
    }

    private static bool IsNeutralWhite(ReadOnlySpan<byte> pixels, int offset)
    {
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        var minimum = Math.Min(red, Math.Min(green, blue));
        var maximum = Math.Max(red, Math.Max(green, blue));
        return minimum >= 120 && maximum - minimum <= 35;
    }

    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;

    private static void ValidateFrame(CapturedFrame frame)
    {
        if (frame.Width < 64 || frame.Height < 64 || frame.Stride < frame.Width * 4 ||
            frame.Pixels.Length < (long)frame.Stride * frame.Height)
            throw new ArgumentException("유효한 BGRA8 캡처 프레임이 필요합니다.", nameof(frame));
    }
}
