using RF4Overlay.Core.Capture;

namespace RF4Overlay.Features.BiteAlarm;

public enum BiteState { Inactive, Monitoring, Alerting, WaitingForDisappearance }
public enum BiteObservation { Present, Absent, CaptureFailed }
public interface IBiteDetector
{
    ValueTask<BiteObservation> DetectAsync(CapturedFrame frame, CancellationToken cancellationToken);
}
public sealed record BiteAlarmOptions(TimeSpan RepeatInterval, TimeSpan DisappearanceConfirmation, float Volume = 0.7f)
{
    public void Validate()
    {
        if (RepeatInterval <= TimeSpan.Zero || DisappearanceConfirmation <= TimeSpan.Zero ||
            !float.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentException("알람 간격, 소멸 확인 시간 또는 음량이 잘못되었습니다.");
    }
}

/// <summary>Pure state transitions; the caller serializes observation, clock and physical input.</summary>
public sealed class BiteStateMachine
{
    private readonly BiteAlarmOptions _options;
    private TimeSpan? _absentSince;
    private TimeSpan _nextAlarm;
    public BiteState State { get; private set; } = BiteState.Inactive;
    public BiteStateMachine(BiteAlarmOptions options) { options.Validate(); _options = options; }
    public void Start() { State = BiteState.Monitoring; _absentSince = null; }
    public void Stop() { State = BiteState.Inactive; _absentSince = null; }
    public bool Observe(BiteObservation observation, TimeSpan now)
    {
        if (State == BiteState.Inactive) return false;
        if (observation == BiteObservation.CaptureFailed) { _absentSince = null; return false; }
        if (State == BiteState.Monitoring && observation == BiteObservation.Present)
        {
            State = BiteState.Alerting; _nextAlarm = now + _options.RepeatInterval; return true;
        }
        if (State == BiteState.WaitingForDisappearance)
        {
            if (observation == BiteObservation.Present) _absentSince = null;
            else
            {
                _absentSince ??= now;
                if (now - _absentSince >= _options.DisappearanceConfirmation)
                { State = BiteState.Monitoring; _absentSince = null; }
            }
        }
        return false;
    }
    public bool Tick(TimeSpan now)
    {
        if (State != BiteState.Alerting || now < _nextAlarm) return false;
        _nextAlarm = now + _options.RepeatInterval; // No burst after a scheduler stall.
        return true;
    }
    public bool Acknowledge()
    {
        if (State != BiteState.Alerting) return false;
        State = BiteState.WaitingForDisappearance;
        _absentSince = null;
        return true;
    }
}
