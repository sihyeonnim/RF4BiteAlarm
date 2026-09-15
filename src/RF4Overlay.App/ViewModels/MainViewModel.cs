using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Core.Settings;
using RF4Overlay.Features.Metronome;

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

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly FeatureCommandDispatcher _runtime;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly MetronomeSettings _settings;
    private readonly Action<IReadOnlyList<HotkeyBinding>> _apply;
    private readonly Action<UserSettings> _save;
    private readonly Dictionary<FeatureId, HotkeySetting> _hotkeys;
    private readonly List<KeyStroke> _recorded = [];
    private FeatureId? _recording;
    private string _notice = "", _inputStatus = "전역 입력 준비 중", _connectionStatus = "RF4 연결: 캡처 준비 전";
    private string _bpmText, _gapText = "500";
    public event EventHandler? RecordingChanged;
    public IReadOnlyList<FeatureViewModel> Features { get; }
    public AsyncCommand CancelRecordingCommand { get; }
    public bool IsRecording => _recording is not null;
    public string Notice { get => _notice; set { _notice = value; Changed(); } }
    public string InputStatus { get => _inputStatus; set { _inputStatus = value; Changed(); } }
    public string ConnectionStatus { get => _connectionStatus; set { _connectionStatus = value; Changed(); } }
    public string BpmText
    {
        get => _bpmText;
        set
        {
            _bpmText = value; Changed();
            if (int.TryParse(value, out var bpm) && bpm is >= 20 and <= 300)
            { _settings.Bpm = bpm; Notice = "BPM을 적용했습니다."; SaveSettings(); }
            else Notice = "BPM은 20–300 사이의 정수로 입력하세요. 이전 유효 값이 유지됩니다.";
        }
    }
    public double Volume
    {
        get => _settings.Volume * 100;
        set { _settings.Volume = (float)Math.Clamp(value / 100, 0, 1); Changed(); SaveSettings(); }
    }
    public string GapText { get => _gapText; set { _gapText = value; Changed(); } }

    public MainViewModel(FeatureCommandDispatcher runtime, MetronomeSettings settings, IEnumerable<HotkeySetting> hotkeys,
        Action<IReadOnlyList<HotkeyBinding>> apply, Action<UserSettings> save)
    {
        _runtime = runtime; _settings = settings; _apply = apply; _save = save; _bpmText = settings.Bpm.ToString();
        _hotkeys = hotkeys.ToDictionary(h => h.Feature);
        Features = runtime.GetStatuses().Select(s => new FeatureViewModel(s, runtime, () => RecordOrSave(s.Id))).ToArray();
        CancelRecordingCommand = new(() => { CancelRecording(); return Task.CompletedTask; });
        apply(Snapshot().Bindings()); RefreshHotkeyText();
        runtime.StatusChanged += OnStatusChanged;
    }
    private UserSettings Snapshot() => new(_settings.Bpm, _settings.Volume, _hotkeys.Values.ToArray());
    public void SaveSettings() => _save(Snapshot());
    private void RecordOrSave(FeatureId id)
    {
        if (_recording == id)
        {
            try
            {
                if (_recorded.Count == 0) throw new ArgumentException("키를 한 번 이상 입력하세요.");
                if (!int.TryParse(GapText, out var gap) || gap is < 100 or > 5000) throw new ArgumentException("키 간격은 100–5000ms입니다.");
                var candidate = _hotkeys.Values.Where(h => h.Feature != id).Append(new(id, _recorded.ToArray(), gap)).ToArray();
                var settings = new UserSettings(_settings.Bpm, _settings.Volume, candidate);
                settings.Validate(); _apply(settings.Bindings());
                _hotkeys[id] = candidate.Last();
                CancelRecording(); SaveSettings(); Notice = "단축키를 저장했습니다.";
            }
            catch (ArgumentException error) { Notice = error.Message; }
            return;
        }
        _recording = id; _recorded.Clear();
        RecordingChanged?.Invoke(this, EventArgs.Empty);
        RefreshHotkeyText();
        Notice = "원하는 키 또는 조합을 차례로 누른 뒤 '저장'을 클릭하세요. 최대 8개, 반복 키도 가능합니다.";
    }
    public void RecordStroke(KeyStroke stroke)
    {
        if (!IsRecording) return;
        if (_recorded.Count >= 8) { Notice = "최대 8개까지 기록할 수 있습니다."; return; }
        _recorded.Add(stroke); RefreshHotkeyText();
    }
    public void CancelRecording()
    {
        _recording = null; _recorded.Clear();
        RecordingChanged?.Invoke(this, EventArgs.Empty); RefreshHotkeyText();
    }
    private void RefreshHotkeyText()
    {
        foreach (var feature in Features)
        {
            var recording = _recording == feature.Id;
            var strokes = recording ? _recorded : _hotkeys.GetValueOrDefault(feature.Id)?.Keys.AsEnumerable() ?? [];
            feature.SetHotkey(string.Join(" → ", strokes.Select(Format)), recording);
        }
    }
    private static string Format(KeyStroke stroke) =>
        (stroke.Modifiers == KeyModifiers.None ? "" : stroke.Modifiers.ToString().Replace(", ", "+") + "+") +
        KeyInterop.KeyFromVirtualKey(stroke.VirtualKey);
    private void OnStatusChanged(object? sender, FeatureStatus status) =>
        _ui.BeginInvoke(() => Features.Single(f => f.Id == status.Id).Update(status));
    public void Dispose() => _runtime.StatusChanged -= OnStatusChanged;
}

public sealed class FeatureViewModel : ObservableObject
{
    private FeatureStatus _status;
    private string _hotkeyText = "", _recordText = "키 기록";
    public FeatureId Id => _status.Id;
    public string Name => _status.Name;
    public string Description => _status.Error ?? _status.Description;
    public string State => _status.State switch
    {
        FeatureState.Unavailable => "사용 불가", FeatureState.Running => "실행 중",
        FeatureState.Stopping => "정리 중", FeatureState.Faulted => "오류", _ => "정지"
    };
    public string HotkeyText => _hotkeyText;
    public string RecordText => _recordText;
    public string RecordAutomationName => Name + " 단축키 기록";
    public string ButtonText => _status.State switch
    {
        FeatureState.Unavailable => "준비 중", FeatureState.Running => "정지", FeatureState.Stopping => "정지 중", _ => "시작"
    };
    public AsyncCommand ToggleCommand { get; }
    public AsyncCommand RecordCommand { get; }
    public FeatureViewModel(FeatureStatus status, FeatureCommandDispatcher runtime, Action record)
    {
        _status = status;
        RecordCommand = new(() => { record(); return Task.CompletedTask; });
        ToggleCommand = new(async () =>
        {
            var result = await runtime.ExecuteAsync(new(Id, FeatureAction.Toggle));
            if (!result.Succeeded) { _status = _status with { Error = result.Message }; Changed(nameof(Description)); }
        }, () => _status.State is not (FeatureState.Unavailable or FeatureState.Stopping));
    }
    public void SetHotkey(string text, bool recording)
    {
        _hotkeyText = text.Length == 0 ? "키 입력 대기" : text; _recordText = recording ? "저장" : "키 기록";
        Changed(nameof(HotkeyText)); Changed(nameof(RecordText));
    }
    public void Update(FeatureStatus status)
    {
        _status = status;
        Changed(nameof(Description)); Changed(nameof(State)); Changed(nameof(ButtonText)); ToggleCommand.Refresh();
    }
}
