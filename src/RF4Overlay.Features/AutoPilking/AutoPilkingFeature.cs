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
    private double _holdSeconds = 1;
    private double _releaseSeconds = 3.5;

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

public sealed class AutoPilkingFeature(
    IInputAutomation automation,
    AutoPilkingSettings settings,
    IGameForegroundGate foreground) : IFeature
{
    private const double HoldRandomizationSeconds = 0.5;

    public FeatureStatus InitialStatus { get; } = new(FeatureId.AutoPilking, "Auto Pilking", FeatureState.Stopped,
        "선택한 입력을 설정한 누름/해제 주기로 반복합니다.");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreground.WaitUntilForegroundAsync(cancellationToken).ConfigureAwait(false);

            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var focusCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var operation = RunCycleAsync(operationCancellation.Token);
            var focusLost = foreground.WaitUntilBackgroundAsync(focusCancellation.Token);
            var completed = await Task.WhenAny(operation, focusLost).ConfigureAwait(false);
            if (completed == focusLost)
            {
                operationCancellation.Cancel();
                try { await operation.ConfigureAwait(false); }
                catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested) { }
            }
            else
            {
                focusCancellation.Cancel();
                await operation.ConfigureAwait(false);
                try { await focusLost.ConfigureAwait(false); }
                catch (OperationCanceledException) when (focusCancellation.IsCancellationRequested) { }
            }
        }

        async Task RunCycleAsync(CancellationToken operationToken)
        {
            var cycle = settings.Snapshot();
            var randomizedHoldSeconds = Math.Clamp(
                cycle.HoldDuration.TotalSeconds + (Random.Shared.NextDouble() * 2 - 1) * HoldRandomizationSeconds,
                AutoPilkingSettings.MinimumSeconds,
                AutoPilkingSettings.MaximumSeconds);
            await automation.HoldAsync(cycle.Input, TimeSpan.FromSeconds(randomizedHoldSeconds), operationToken)
                .ConfigureAwait(false);
            await Task.Delay(cycle.ReleaseDuration, operationToken).ConfigureAwait(false);
        }
    }
}

public sealed class UnavailableAutoPilkingFeature() : PlannedFeature(
    FeatureId.AutoPilking, "Auto Pilking", "자동 입력 서비스가 필요합니다.");
