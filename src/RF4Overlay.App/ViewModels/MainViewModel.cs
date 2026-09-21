using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Core.Settings;
using RF4Overlay.Core.Audio;
using RF4Overlay.Features.BiteAlarm;
using RF4Overlay.Features.Metronome;
using RF4Overlay.Features.AutoPilking;
using RF4Overlay.Features.LeftClickHold;

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
    private readonly AutoPilkingSettings _autoSettings;
    private readonly BiteAlarmSettings _biteSettings;
    private readonly VoiceAnnouncementSettings _voiceSettings;
    private readonly LeftClickHoldSettings _leftClickSettings;
    private readonly IAudioService _audio;
    private IAudioVoice? _alarmPreview;
    public AsyncCommand TestAlarmCommand { get; }
    private readonly Action<IReadOnlyList<HotkeyBinding>> _apply;
    private readonly Action<UserSettings> _save;
    private readonly Dictionary<FeatureId, HotkeySetting> _hotkeys;
    private readonly List<KeyStroke> _recorded = [];
    private FeatureId? _recording;
    private string _notice = "", _inputStatus = "전역 입력 준비 중", _connectionStatus = "RF4 연결: 캡처 준비 전";
    private string _bpmText, _periodText, _holdText, _releaseText, _gapText = "500";
    private AutomationInputOption _selectedAutoInput;
    private bool _isPeriodMode = true;
    public event EventHandler? RecordingChanged;
    public IReadOnlyList<FeatureViewModel> Features { get; }
    public AsyncCommand CancelRecordingCommand { get; }
    public AsyncCommand IncreaseHoldCommand { get; }
    public AsyncCommand DecreaseHoldCommand { get; }
    public AsyncCommand IncreaseReleaseCommand { get; }
    public AsyncCommand DecreaseReleaseCommand { get; }
    public IReadOnlyList<AutomationInputOption> AutoInputOptions { get; } = CreateAutoInputOptions();
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
            if (TryNumber(value, out var bpm) && bpm is >= 1 and <= 300)
            {
                _settings.Bpm = bpm;
                _periodText = FormatNumber(_settings.PeriodSeconds);
                Changed(nameof(PeriodText));
                Notice = "BPM을 적용했습니다.";
                SaveSettings();
            }
            else Notice = "BPM은 1–300 사이의 숫자로 입력하세요. 이전 유효 값이 유지됩니다.";
        }
    }
    public string PeriodText
    {
        get => _periodText;
        set
        {
            _periodText = value; Changed();
            if (TryNumber(value, out var seconds) && seconds is >= MetronomeSettings.MinimumPeriodSeconds and <= MetronomeSettings.MaximumPeriodSeconds)
            {
                _settings.PeriodSeconds = seconds;
                _bpmText = FormatNumber(_settings.Bpm);
                Changed(nameof(BpmText));
                Notice = "소리 주기를 적용했습니다.";
                SaveSettings();
            }
            else Notice = "주기는 0.2–60초 사이의 숫자로 입력하세요. 이전 유효 값이 유지됩니다.";
        }
    }
    public double Volume
    {
        get => _settings.Volume * 100;
        set { _settings.Volume = (float)Math.Clamp(value / 100, 0, 1); Changed(); SaveSettings(); }
    }
    public IReadOnlyList<AlarmSoundOption> AlarmSounds { get; } =
    [new("Default", SoundCue.Alarm), new("알람 1", SoundCue.Sound8), new("알람 2", SoundCue.Sound0),
     new("알람 3", SoundCue.Sound9), new("알람 4", SoundCue.TradeReceived)];
    public SoundCue AlarmSound
    {
        get => _biteSettings.Current.Sound;
        set { _biteSettings.Current = _biteSettings.Current with { Sound = value }; Changed(); SaveSettings(); }
    }
    public double AlarmVolume
    {
        get => _biteSettings.Current.Volume * 100;
        set { _biteSettings.Current = _biteSettings.Current with { Volume = (float)Math.Clamp(value / 100, 0, 1) }; Changed(); SaveSettings(); }
    }
    public bool VoiceAnnouncementEnabled
    {
        get => _voiceSettings.Current.Enabled;
        set
        {
            _voiceSettings.Current = _voiceSettings.Current with { Enabled = value };
            Changed(); SaveSettings();
            Notice = value ? "음성 안내를 켰습니다." : "음성 안내를 껐습니다.";
        }
    }
    public double VoiceAnnouncementVolume
    {
        get => _voiceSettings.Current.Volume;
        set
        {
            _voiceSettings.Current = _voiceSettings.Current with { Volume = (int)Math.Round(Math.Clamp(value, 0, 100)) };
            Changed(); SaveSettings();
        }
    }
    public bool IsPeriodMode
    {
        get => _isPeriodMode;
        set
        {
            if (_isPeriodMode == value) return;
            _isPeriodMode = value;
            Changed();
            Changed(nameof(IsBpmMode));
        }
    }
    public bool IsBpmMode
    {
        get => !_isPeriodMode;
        set { if (value) IsPeriodMode = false; }
    }
    public bool ShiftLeftClickHold
    {
        get => _leftClickSettings.WithShift;
        set { _leftClickSettings.WithShift = value; Changed(); SaveSettings(); }
    }
    public string GapText { get => _gapText; set { _gapText = value; Changed(); } }
    public AutomationInputOption SelectedAutoInput
    {
        get => _selectedAutoInput;
        set
        {
            if (value is null || value == _selectedAutoInput) return;
            _selectedAutoInput = value;
            _autoSettings.Input = value.Input;
            Changed(); SaveSettings(); Notice = "Auto Pilking 입력을 적용했습니다.";
        }
    }
    public string HoldText
    {
        get => _holdText;
        set
        {
            _holdText = value; Changed();
            if (TryAutoSeconds(value, out var seconds))
            {
                _autoSettings.HoldSeconds = seconds; SaveSettings(); Notice = "누름 시간을 적용했습니다.";
            }
            else Notice = "누름 시간은 0.1–60초 사이의 숫자로 입력하세요.";
        }
    }
    public string ReleaseText
    {
        get => _releaseText;
        set
        {
            _releaseText = value; Changed();
            if (TryAutoSeconds(value, out var seconds))
            {
                _autoSettings.ReleaseSeconds = seconds; SaveSettings(); Notice = "떼는 시간을 적용했습니다.";
            }
            else Notice = "떼는 시간은 0.1–60초 사이의 숫자로 입력하세요.";
        }
    }

    public MainViewModel(FeatureCommandDispatcher runtime, MetronomeSettings settings, AutoPilkingSettings autoSettings,
        BiteAlarmSettings biteSettings, VoiceAnnouncementSettings voiceSettings, LeftClickHoldSettings leftClickSettings,
        IEnumerable<HotkeySetting> hotkeys,
        Action<IReadOnlyList<HotkeyBinding>> apply, Action<UserSettings> save, IAudioService audio)
    {
        _audio = audio;
        _biteSettings = biteSettings;
        _voiceSettings = voiceSettings;
        _leftClickSettings = leftClickSettings;
        TestAlarmCommand = new(() =>
        {
            try
            {
                _alarmPreview ??= _audio.CreateVoice();
                _alarmPreview.Stop();
                var selected = _biteSettings.Current;
                _alarmPreview.Play(selected.Sound, selected.Volume);
                Notice = "선택한 알람 소리를 한 번 재생합니다.";
            }
            catch (Exception error)
            {
                _alarmPreview?.Dispose();
                _alarmPreview = null;
                Notice = "알람 테스트 실패: " + error.Message;
            }
            return Task.CompletedTask;
        });
        _runtime = runtime; _settings = settings; _autoSettings = autoSettings; _apply = apply; _save = save;
        _bpmText = FormatNumber(settings.Bpm); _periodText = FormatNumber(settings.PeriodSeconds);
        _holdText = FormatNumber(autoSettings.HoldSeconds); _releaseText = FormatNumber(autoSettings.ReleaseSeconds);
        _selectedAutoInput = AutoInputOptions.Single(option => option.Input == autoSettings.Input);
        _hotkeys = hotkeys.ToDictionary(h => h.Feature);
        Features = runtime.GetStatuses().Select(s => new FeatureViewModel(s, runtime, () => RecordOrSave(s.Id))).ToArray();
        CancelRecordingCommand = new(() => { CancelRecording(); return Task.CompletedTask; });
        IncreaseHoldCommand = StepCommand(true, 0.1);
        DecreaseHoldCommand = StepCommand(true, -0.1);
        IncreaseReleaseCommand = StepCommand(false, 0.1);
        DecreaseReleaseCommand = StepCommand(false, -0.1);
        apply(Snapshot().Bindings()); RefreshHotkeyText();
        runtime.StatusChanged += OnStatusChanged;
    }
    private UserSettings Snapshot() => new(_settings.Bpm, _settings.Volume, _hotkeys.Values.ToArray(), _settings.PeriodSeconds,
        new(_autoSettings.Input, _autoSettings.HoldSeconds, _autoSettings.ReleaseSeconds), BiteAlarm: _biteSettings.Current,
        ShiftLeftClickHold: _leftClickSettings.WithShift, VoiceAnnouncement: _voiceSettings.Current,
        SettingsFormatVersion: UserSettings.CurrentSettingsFormatVersion);
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
                var settings = new UserSettings(_settings.Bpm, _settings.Volume, candidate, _settings.PeriodSeconds,
                    new(_autoSettings.Input, _autoSettings.HoldSeconds, _autoSettings.ReleaseSeconds), BiteAlarm: _biteSettings.Current,
                    ShiftLeftClickHold: _leftClickSettings.WithShift, VoiceAnnouncement: _voiceSettings.Current,
                    SettingsFormatVersion: UserSettings.CurrentSettingsFormatVersion);
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
    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
    private static bool TryAutoSeconds(string value, out double number) =>
        TryNumber(value, out number) && number is >= AutoPilkingSettings.MinimumSeconds and <= AutoPilkingSettings.MaximumSeconds;
    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private AsyncCommand StepCommand(bool hold, double delta) => new(() =>
    {
        var current = hold ? _autoSettings.HoldSeconds : _autoSettings.ReleaseSeconds;
        var next = Math.Round(Math.Clamp(current + delta, AutoPilkingSettings.MinimumSeconds, AutoPilkingSettings.MaximumSeconds), 1);
        if (hold) HoldText = FormatNumber(next); else ReleaseText = FormatNumber(next);
        return Task.CompletedTask;
    });
    private static IReadOnlyList<AutomationInputOption> CreateAutoInputOptions()
    {
        var options = new List<AutomationInputOption> { new("마우스 우클릭", AutomationInput.MouseRight) };
        options.AddRange(Enumerable.Range('A', 26).Select(key => new AutomationInputOption(((char)key).ToString(), AutomationInput.Keyboard((byte)key))));
        options.AddRange(Enumerable.Range('0', 10).Select(key => new AutomationInputOption(((char)key).ToString(), AutomationInput.Keyboard((byte)key))));
        options.AddRange(Enumerable.Range(0, 10).Select(key => new AutomationInputOption($"NumPad {key}", AutomationInput.Keyboard((byte)(0x60 + key)))));
        options.Add(new("Space", AutomationInput.Keyboard(0x20)));
        options.Add(new("Enter", AutomationInput.Keyboard(0x0D)));
        options.Add(new("왼쪽 화살표", AutomationInput.Keyboard(0x25)));
        options.Add(new("위쪽 화살표", AutomationInput.Keyboard(0x26)));
        options.Add(new("오른쪽 화살표", AutomationInput.Keyboard(0x27)));
        options.Add(new("아래쪽 화살표", AutomationInput.Keyboard(0x28)));
        return options;
    }
    private void OnStatusChanged(object? sender, FeatureStatus status) =>
        _ui.BeginInvoke(() => Features.Single(f => f.Id == status.Id).Update(status));
    public void Dispose()
    {
        _runtime.StatusChanged -= OnStatusChanged;
        _alarmPreview?.Dispose();
        _alarmPreview = null;
    }
}

public sealed record AlarmSoundOption(string Name, SoundCue Sound);

public sealed record AutomationInputOption(string Name, AutomationInput Input)
{
    public override string ToString() => Name;
}

public sealed class FeatureViewModel : ObservableObject
{
    private FeatureStatus _status;
    private string _hotkeyText = "", _recordText = "⌨";
    public FeatureId Id => _status.Id;
    public string Name => _status.Name;
    public bool IsMetronome => Id == FeatureId.Metronome;
    public bool IsAutoPilking => Id == FeatureId.AutoPilking;
    public bool IsBiteAlarm => Id == FeatureId.BiteAlarm;
    public bool IsLeftClickHold => Id == FeatureId.LeftClickHold;
    public bool HasSettings => IsMetronome || IsAutoPilking || IsBiteAlarm || IsLeftClickHold;
    public string SettingsAutomationName => Name + " 설정";
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
        _hotkeyText = text.Length == 0 ? "키 입력 대기" : text; _recordText = recording ? "✓" : "⌨";
        Changed(nameof(HotkeyText)); Changed(nameof(RecordText));
    }
    public void Update(FeatureStatus status)
    {
        _status = status;
        Changed(nameof(Description)); Changed(nameof(State)); Changed(nameof(ButtonText)); ToggleCommand.Refresh();
    }
}
