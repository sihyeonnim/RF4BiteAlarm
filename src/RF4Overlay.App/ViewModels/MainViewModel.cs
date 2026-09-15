using System.Windows.Input;
using RF4Overlay.Core.Features;

namespace RF4Overlay.App.ViewModels;

public sealed class MainViewModel(FeatureCommandDispatcher dispatcher)
{
    public IReadOnlyList<FeatureViewModel> Features { get; } = dispatcher.GetStatuses()
        .Select(status => new FeatureViewModel(status, dispatcher)).ToArray();
}

// Phase 1 statuses are immutable. Add notifications when live feature lifecycles arrive.
public sealed class FeatureViewModel(FeatureStatus status, FeatureCommandDispatcher dispatcher)
{
    public string Name => status.Name;
    public string Description => status.Description;
    public ICommand ToggleCommand { get; } = new FeatureToggleCommand(status, dispatcher);
}

internal sealed class FeatureToggleCommand(FeatureStatus status, FeatureCommandDispatcher dispatcher) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => status.State != FeatureState.Unavailable;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            dispatcher.Execute(new(status.Id, FeatureAction.Toggle));
    }
}
