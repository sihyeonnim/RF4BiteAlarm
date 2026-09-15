namespace RF4Overlay.Core.Features;

public enum FeatureId { BiteAlarm, Metronome, AutoPilking }
public enum FeatureAction { Start, Stop, Toggle }
public enum FeatureState { Unavailable, Stopped, Running }

public sealed record FeatureStatus(FeatureId Id, string Name, FeatureState State, string Description);
public sealed record FeatureCommand(FeatureId Feature, FeatureAction Action);
public sealed record FeatureCommandResult(bool Succeeded, string Message);

public interface IFeature
{
    FeatureStatus Status { get; }
    FeatureCommandResult Execute(FeatureAction action);
}

/// <summary>Single entry point for UI, tray and global hotkey commands.</summary>
public sealed class FeatureCommandDispatcher(IEnumerable<IFeature> features)
{
    private readonly IReadOnlyDictionary<FeatureId, IFeature> _features =
        features.ToDictionary(feature => feature.Status.Id);

    public IReadOnlyList<FeatureStatus> GetStatuses() =>
        _features.Values.Select(feature => feature.Status).ToArray();

    public FeatureCommandResult Execute(FeatureCommand command) =>
        _features.TryGetValue(command.Feature, out var feature)
            ? feature.Execute(command.Action)
            : new(false, "등록되지 않은 기능입니다.");
}
