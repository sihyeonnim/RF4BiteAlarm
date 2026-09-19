using System.Windows;
using System.Windows.Input;
using RF4Overlay.App.ViewModels;
using RF4Overlay.Core.Features;
using RF4Overlay.Core.Input;
using RF4Overlay.Core.Settings;
using RF4Overlay.Features;
using RF4Overlay.Features.Metronome;
using RF4Overlay.Infrastructure.Audio;
using RF4Overlay.Infrastructure.Input;
using RF4Overlay.Infrastructure.Settings;
using RF4Overlay.Infrastructure.Tray;
using RF4Overlay.Infrastructure.Capture;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RF4Overlay.App;

public partial class MainWindow : Window
{
    private readonly FeatureCommandDispatcher _runtime;
    private readonly WindowsInputMonitor _input = new();
    private readonly GlobalHotkeyService _hotkeys;
    private readonly SettingsStore _store = new();
    private readonly CaptureMonitor _capture = new(new GameWindowLocator());
    private readonly MainViewModel _viewModel;
    private TrayService? _tray;
    private bool _exiting;
    private Task? _exitTask;
    private Task _startupTask = Task.CompletedTask;
    private Task<string>? _snapshotTask;
    public MainWindow()
    {
        InitializeComponent();
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Top = SystemParameters.WorkArea.Top;
        UserSettings saved;
        string? warning = null;
        try { saved = _store.Load(); }
        catch (Exception error) { saved = UserSettings.Default; warning = "설정을 읽지 못해 기본값을 사용합니다: " + error.Message; }
        var settings = new MetronomeSettings { PeriodSeconds = saved.EffectivePeriodSeconds, Volume = saved.Volume };
        _runtime = new(FeatureCatalog.Create(new AudioService(), settings, _input, _capture));
        _hotkeys = new(_input, _runtime);
        _viewModel = new(_runtime, settings, saved.Hotkeys, bindings => _hotkeys.SetBindings(bindings), PersistSettings);
        DataContext = _viewModel;
        _store.SaveFailed += (_, message) => Dispatcher.BeginInvoke(() => _viewModel.Notice = "설정 저장 실패: " + message);
        _capture.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() =>
            _viewModel.ConnectionStatus = status.Message + (status.FrameCount > 0 ? $" · {status.Width}×{status.Height} · {status.FrameCount} frames" : ""));
        _viewModel.Notice = warning ?? "단축키를 누르면 게임에도 같은 키가 전달됩니다.";
        _hotkeys.Error += (_, message) => Dispatcher.BeginInvoke(() => _viewModel.Notice = message);
        _viewModel.RecordingChanged += (_, _) => _hotkeys.Suspended = _viewModel.IsRecording;
        Loaded += OnLoaded;
        Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            _viewModel.CancelRecording();
            if (_tray is null) _ = ExitAsync();
            else Hide();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (!_viewModel.IsRecording || e.IsRepeat) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var vk = (byte)KeyInterop.VirtualKeyFromKey(key);
            if (InputPolicy.IsModifier(vk)) return;
            var modifiers = KeyModifiers.None;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= KeyModifiers.Control;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= KeyModifiers.Windows;
            _viewModel.RecordStroke(new(vk, modifiers));
            e.Handled = true;
        };
    }
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        _startupTask = InitializeServicesAsync();
    }
    private async Task InitializeServicesAsync()
    {
        try { _tray = new(_runtime, action => Dispatcher.BeginInvoke(action), ShowMain, () => _ = ExitAsync()); }
        catch (Exception error) { _viewModel.Notice = "Tray를 만들지 못했습니다: " + error.Message; }
        try { await _input.StartAsync(); _viewModel.InputStatus = "전역 입력 관찰 중 · injected 입력 제외"; }
        catch (Exception error) { _viewModel.InputStatus = "단축키 사용 불가: " + error.Message; }
        _capture.Start();
    }
    private void PersistSettings(UserSettings settings)
    {
        try { _store.Save(settings); }
        catch (Exception error) { _viewModel.Notice = "설정 저장 실패: " + error.Message; }
    }
    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ExitClick(object sender, RoutedEventArgs e) => _ = ExitAsync();
    private async void SnapshotClick(object sender, RoutedEventArgs e)
    {
        if (_snapshotTask is { IsCompleted: false }) return;
        var frame = _capture.LatestFrame;
        if (frame is null) { _viewModel.Notice = "저장할 캡처 프레임이 없습니다."; return; }
        try
        {
            _snapshotTask = Task.Run(() =>
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RF4Overlay", "diagnostics");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels.ToArray(), frame.Stride);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path); encoder.Save(stream);
                return path;
            });
            _viewModel.Notice = "진단 이미지 저장: " + await _snapshotTask;
        }
        catch (Exception error) { _viewModel.Notice = "진단 저장 실패: " + error.Message; }
    }
    private Task ExitAsync() => _exitTask ??= ExitCoreAsync();
    private async Task ExitCoreAsync()
    {
        IsEnabled = false;
        _viewModel.CancelRecording();
        _viewModel.SaveSettings();
        // Stop command producers, then features, then their input/capture sources.
        try
        {
            await _startupTask;
            _tray?.Dispose();
            await _hotkeys.DisposeAsync();
            await _runtime.DisposeAsync();
            await _input.DisposeAsync();
            await _capture.DisposeAsync();
            await _store.DisposeAsync();
            if (_snapshotTask is not null) { try { await _snapshotTask; } catch { /* Already shown by SnapshotClick. */ } }
            _viewModel.Dispose();
            _exiting = true;
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            _viewModel.Notice = "종료 정리 실패: " + error.Message;
            IsEnabled = true;
            _exitTask = null;
        }
    }
}
