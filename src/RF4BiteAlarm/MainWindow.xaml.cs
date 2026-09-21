using System.Windows;
using System.Windows.Input;
using RF4BiteAlarm.Infrastructure;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Core.Settings;
using RF4Overlay.Features.BiteAlarm;
using RF4Overlay.Infrastructure.Audio;
using RF4Overlay.Infrastructure.Capture;
using RF4Overlay.Infrastructure.Input;

namespace RF4BiteAlarm;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly AudioService _audio = new();
    private readonly WindowsInputMonitor _input = new();
    private readonly CaptureMonitor _capture = new(new GameWindowLocator());
    private readonly HotkeyMatcher _hotkeyMatcher = new();
    private readonly BiteAlarmSettings _alarmSettings = new();
    private readonly FeatureCommandDispatcher _runtime;
    private IAudioVoice? _preview;
    private AppSettings _settings = AppSettings.Default;
    private TrayService? _tray;
    private bool _ready;
    private bool _recording;
    private bool _exiting;
    private Task? _exitTask;

    public MainWindow()
    {
        InitializeComponent();
        try { _settings = _store.Load(); }
        catch (Exception error)
        {
            _settings = AppSettings.Default;
            ConnectionText.Text = "설정을 읽지 못해 기본값을 사용합니다: " + error.Message;
        }

        _alarmSettings.Current = _settings.Alarm;
        _runtime = new FeatureCommandDispatcher([
            new BiteAlarmFeature(_audio, _input, _capture, () => new FishCaughtIconDetector(),
                new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)), _alarmSettings)
        ]);
        SoundBox.ItemsSource = new[]
        {
            new SoundOption("Default", SoundCue.Alarm),
            new SoundOption("알람 1", SoundCue.Sound8),
            new SoundOption("알람 2", SoundCue.Sound0),
            new SoundOption("알람 3", SoundCue.Sound9),
            new SoundOption("알람 4", SoundCue.TradeReceived)
        };
        SoundBox.SelectedValue = _settings.Alarm.Sound;
        VolumeSlider.Value = _settings.Alarm.Volume * 100;
        UpdateHotkey();
        BindHotkey();

        _runtime.StatusChanged += RuntimeStatusChanged;
        _capture.StatusChanged += CaptureStatusChanged;
        _input.KeyReceived += InputKeyReceived;
        PreviewKeyDown += WindowPreviewKeyDown;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
        _ready = true;
        UpdateAlarmSetting();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowLoaded;
        try
        {
            _tray = new TrayService(
                () => Dispatcher.BeginInvoke(ShowMain),
                () => Dispatcher.BeginInvoke(() => _ = ToggleAsync()),
                () => Dispatcher.BeginInvoke(() => _ = ExitAsync()));
        }
        catch (Exception error) { ConnectionText.Text = "트레이 아이콘을 만들지 못했습니다: " + error.Message; }
        try { await _input.StartAsync(); }
        catch (Exception error) { ConnectionText.Text = "전역 단축키를 시작하지 못했습니다: " + error.Message; }
        _capture.Start();
    }

    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        Hide();
    }

    private void SoundChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_ready) UpdateAlarmSetting();
    }

    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
        if (_ready) UpdateAlarmSetting();
    }

    private void UpdateAlarmSetting()
    {
        if (SoundBox.SelectedValue is not SoundCue cue) return;
        var alarm = new BiteAlarmSetting(cue, (float)(VolumeSlider.Value / 100));
        _alarmSettings.Current = alarm;
        _settings = _settings with { Alarm = alarm };
        SaveSettings();
    }

    private void TestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _preview ??= _audio.CreateVoice();
            _preview.Stop();
            _preview.Play(_alarmSettings.Current.Sound, _alarmSettings.Current.Volume);
        }
        catch (Exception error) { ConnectionText.Text = "알람을 재생하지 못했습니다: " + error.Message; }
    }

    private void RecordClick(object sender, RoutedEventArgs e)
    {
        _recording = true;
        RecordButton.Content = "입력 중";
        HotkeyText.Text = "새 단축키를 누르세요 (Esc 취소)";
        Activate();
        Focus();
    }

    private void WindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_recording || e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            EndRecording();
            e.Handled = true;
            return;
        }
        var virtualKey = (byte)KeyInterop.VirtualKeyFromKey(key);
        if (InputPolicy.IsModifier(virtualKey)) return;
        var modifiers = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= KeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= KeyModifiers.Windows;
        _settings = _settings with { Hotkey = new KeyStroke(virtualKey, modifiers) };
        BindHotkey();
        SaveSettings();
        EndRecording();
        e.Handled = true;
    }

    private void EndRecording()
    {
        _recording = false;
        RecordButton.Content = "변경";
        UpdateHotkey();
    }

    private void BindHotkey() => _hotkeyMatcher.SetBindings([
        new HotkeyBinding([_settings.Hotkey], TimeSpan.FromMilliseconds(500),
            new(FeatureId.BiteAlarm, FeatureAction.Toggle))
    ]);

    private void UpdateHotkey() => HotkeyText.Text = FormatHotkey(_settings.Hotkey);

    private static string FormatHotkey(KeyStroke stroke)
    {
        var pieces = new List<string>();
        if (stroke.Modifiers.HasFlag(KeyModifiers.Control)) pieces.Add("Ctrl");
        if (stroke.Modifiers.HasFlag(KeyModifiers.Alt)) pieces.Add("Alt");
        if (stroke.Modifiers.HasFlag(KeyModifiers.Shift)) pieces.Add("Shift");
        if (stroke.Modifiers.HasFlag(KeyModifiers.Windows)) pieces.Add("Win");
        pieces.Add(KeyInterop.KeyFromVirtualKey(stroke.VirtualKey).ToString());
        return string.Join(" + ", pieces);
    }

    private void InputKeyReceived(object? sender, KeyInput input)
    {
        if (_recording) return;
        if (_hotkeyMatcher.Process(input) is not null)
            Dispatcher.BeginInvoke(() => _ = ToggleAsync());
    }

    private void RuntimeStatusChanged(object? sender, FeatureStatus status) => Dispatcher.BeginInvoke(() =>
    {
        var running = status.State == FeatureState.Running;
        StateText.Text = status.State switch
        {
            FeatureState.Running => "실행 중",
            FeatureState.Stopping => "정지 중",
            FeatureState.Faulted => "오류: " + status.Error,
            _ => "정지됨"
        };
        ToggleButton.Content = running ? "정지" : "시작";
        ToggleButton.IsEnabled = status.State != FeatureState.Stopping;
        _tray?.SetRunning(running);
    });

    private void CaptureStatusChanged(object? sender, RF4Overlay.Core.Capture.CaptureStatus status) =>
        Dispatcher.BeginInvoke(() => ConnectionText.Text = status.Message);

    private async Task ToggleAsync()
    {
        var result = await _runtime.ExecuteAsync(new(FeatureId.BiteAlarm, FeatureAction.Toggle));
        if (!result.Succeeded) ConnectionText.Text = result.Message;
    }

    private void ToggleClick(object sender, RoutedEventArgs e) => _ = ToggleAsync();
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void ExitClick(object sender, RoutedEventArgs e) => _ = ExitAsync();

    private void ShowMain()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void SaveSettings()
    {
        try { _store.Save(_settings); }
        catch (Exception error) { ConnectionText.Text = "설정을 저장하지 못했습니다: " + error.Message; }
    }

    private Task ExitAsync() => _exitTask ??= ExitCoreAsync();

    private async Task ExitCoreAsync()
    {
        try
        {
            IsEnabled = false;
            _tray?.Dispose();
            _preview?.Dispose();
            await _runtime.DisposeAsync();
            await _input.DisposeAsync();
            await _capture.DisposeAsync();
            _exiting = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            ConnectionText.Text = "종료 정리 실패: " + error.Message;
            IsEnabled = true;
            _exitTask = null;
        }
    }

    private sealed record SoundOption(string Name, SoundCue Cue);
}
