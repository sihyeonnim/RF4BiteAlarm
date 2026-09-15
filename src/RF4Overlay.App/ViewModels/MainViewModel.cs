using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using RF4Overlay.Core.Features;

namespace RF4Overlay.App.ViewModels;

public class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _busy;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _busy = true; Refresh();
        try { await execute(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        finally { _busy = false; Refresh(); }
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MainViewModel : IDisposable
{
    private readonly FeatureCommandDispatcher _runtime;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    public IReadOnlyList<FeatureViewModel> Features { get; }
    public MainViewModel(FeatureCommandDispatcher runtime)
    {
        _runtime = runtime;
        Features = runtime.GetStatuses().Select(s => new FeatureViewModel(s, runtime)).ToArray();
        runtime.StatusChanged += OnStatusChanged;
    }
    private void OnStatusChanged(object? sender, FeatureStatus status) =>
        _ui.BeginInvoke(() => Features.Single(f => f.Id == status.Id).Update(status));
    public void Dispose() => _runtime.StatusChanged -= OnStatusChanged;
}

public sealed class FeatureViewModel : ObservableObject
{
    private FeatureStatus _status;
    public FeatureId Id => _status.Id;
    public string Name => _status.Name;
    public string Description => _status.Error ?? _status.Description;
    public string State => _status.State.ToString();
    public string ButtonText => _status.State switch
    {
        FeatureState.Unavailable => "준비 중",
        FeatureState.Running => "정지",
        FeatureState.Stopping => "정지 중",
        _ => "시작"
    };
    public AsyncCommand ToggleCommand { get; }
    public FeatureViewModel(FeatureStatus status, FeatureCommandDispatcher runtime)
    {
        _status = status;
        ToggleCommand = new(async () =>
        {
            var result = await runtime.ExecuteAsync(new(Id, FeatureAction.Toggle));
            if (!result.Succeeded) { _status = _status with { Error = result.Message }; Changed(nameof(Description)); }
        }, () => _status.State is not (FeatureState.Unavailable or FeatureState.Stopping));
    }
    public void Update(FeatureStatus status)
    {
        _status = status;
        Changed(nameof(Description)); Changed(nameof(State)); Changed(nameof(ButtonText)); ToggleCommand.Refresh();
    }
}
