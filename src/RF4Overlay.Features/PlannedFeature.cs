using RF4Overlay.Core.Features;

namespace RF4Overlay.Features;

public abstract class PlannedFeature(FeatureId id, string name, string description) : IFeature
{
    public FeatureStatus InitialStatus { get; } = new(id, name, FeatureState.Unavailable, description);
    public Task RunAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(InitialStatus.Description);
}
