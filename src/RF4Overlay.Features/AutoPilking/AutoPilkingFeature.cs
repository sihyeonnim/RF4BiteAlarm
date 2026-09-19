using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;

namespace RF4Overlay.Features.AutoPilking;

public sealed record AutoPilkingCycle(AutomationInput Input, TimeSpan HoldDuration, TimeSpan ReleaseDuration);

public sealed class AutoPilkingSettings
{
    public const double MinimumSeconds = 0.1;
    public const double MaximumSeconds = 60;
    private readonly object _gate = new();
    private AutomationInput _input = AutomationInput.MouseRight;
    private double _holdSeconds = 3;
    private double _releaseSeconds = 3;

    public AutomationInput Input { get { lock (_gate) return _input; } set { value.Validate(); lock (_gate) _input = value; } }
    public double HoldSeconds { get { lock (_gate) return _holdSeconds; } set { ValidateSeconds(value); lock (_gate) _holdSeconds = value; } }
    public double ReleaseSeconds { get { lock (_gate) return _releaseSeconds; } set { ValidateSeconds(value); lock (_gate) _releaseSeconds = value; } }
    public AutoPilkingCycle Snapshot()
    {
        lock (_gate) return new(_input, TimeSpan.FromSeconds(_holdSeconds), TimeSpan.FromSeconds(_releaseSeconds));
    }
    private static void ValidateSeconds(double value)
    {
        if (!double.IsFinite(value) || value is < MinimumSeconds or > MaximumSeconds)
            throw new ArgumentOutOfRangeException(nameof(value), "시간은 0.1–60초입니다.");
    }
}

public sealed class AutoPilkingFeature(IInputAutomation automation, AutoPilkingSettings settings) : IFeature
{
    public FeatureStatus InitialStatus { get; } = new(FeatureId.AutoPilking, "Auto Pilking", FeatureState.Stopped,
        "선택한 입력을 설정한 누름/해제 주기로 반복합니다.");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cycle = settings.Snapshot();
            await automation.HoldAsync(cycle.Input, cycle.HoldDuration, cancellationToken).ConfigureAwait(false);
            await Task.Delay(cycle.ReleaseDuration, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class UnavailableAutoPilkingFeature() : PlannedFeature(
    FeatureId.AutoPilking, "Auto Pilking", "자동 입력 서비스가 필요합니다.");
