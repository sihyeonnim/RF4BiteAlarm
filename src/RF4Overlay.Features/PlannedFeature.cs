using RF4Overlay.Core.Features;

namespace RF4Overlay.Features;

/// <summary>Explicitly unavailable until a real implementation replaces the placeholder.</summary>
public abstract class PlannedFeature(FeatureId id, string name, string description) : IFeature
{
    public FeatureStatus Status { get; } = new(id, name, FeatureState.Unavailable, description);
    public FeatureCommandResult Execute(FeatureAction action) => new(false, Status.Description);
}
