using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Capture;
using RF4Overlay.Infrastructure.Audio;
using RF4Overlay.Infrastructure.Capture;
using RF4Overlay.Infrastructure.Input;
using RF4Overlay.Infrastructure.Settings;
using RF4Overlay.Core.Settings;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using RF4Overlay.Core.Input;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace RF4Overlay.WindowsTests;

/// <summary>Requires an unlocked interactive Windows desktop and WGC capable graphics device.</summary>
public sealed class WindowsIntegrationTests
{
    [Fact]
    public Task WgcCapturesOccludedWindowResizesAndReleasesOnClose() => OnDesktop(async () =>
    {
        var target = new Window { Title = "RF4 WGC integration target", Width = 320, Height = 240, Left = 30, Top = 30,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.Blue, ShowInTaskbar = false };
        var cover = new Window { Title = "RF4 WGC occlusion test", Width = 340, Height = 260, Left = 20, Top = 20,
            WindowStyle = WindowStyle.None, Background = Brushes.Red, ShowInTaskbar = false, Topmost = true };
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var bright = false;
        animation.Tick += (_, _) => { bright = !bright; target.Background = new SolidColorBrush(Color.FromRgb(0, 0, bright ? (byte)255 : (byte)220)); };
        await using var capture = new WgcWindowCapture();
        try
        {
            target.Show();
            animation.Start();
            await Task.Delay(200);
            var handle = new WindowInteropHelper(target).Handle;
            await using var frames = capture.CaptureAsync(handle, cancel.Token).GetAsyncEnumerator();
            Assert.True(await frames.MoveNextAsync());
            AssertBlue(frames.Current);
            var originalWidth = frames.Current.Width;
            cover.Show();
            // Keep the target changing underneath the cover so WGC must produce new frames.
            target.Width = 400; target.Height = 300;
            var resized = false;
            for (var i = 0; i < 60 && await frames.MoveNextAsync(); i++)
            {
                if (frames.Current.Width <= originalWidth) continue;
                AssertBlue(frames.Current);
                resized = true; break;
            }
            Assert.True(resized, "No resized, occluded frame received.");
            target.Close();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await frames.MoveNextAsync(); });
        }
        finally { animation.Stop(); cover.Close(); target.Close(); }
    });

    [Fact]
    public async Task WindowsHooksStartAndDisposeIdempotently()
    {
        var monitor = new WindowsInputMonitor();
        await Task.WhenAll(monitor.StartAsync(), monitor.StartAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(monitor.DisposeAsync().AsTask(), monitor.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.StartAsync());
    }

    [Fact]
    public Task SendInputHoldsAndReleasesRightMouseAndSingleKey() => OnDesktop(async () =>
    {
        var surface = new Border { Width = 240, Height = 160, Background = Brushes.DarkSlateGray, Focusable = true };
        var window = new Window
        {
            Title = "RF4 input automation target", Width = 260, Height = 190,
            Left = 80, Top = 80, Content = surface, ShowInTaskbar = false, Topmost = true
        };
        var rightDown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rightUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keyDown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keyUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.PreviewMouseRightButtonDown += (_, _) => rightDown.TrySetResult();
        surface.PreviewMouseRightButtonUp += (_, _) => rightUp.TrySetResult();
        surface.PreviewKeyDown += (_, args) => { if (args.Key == Key.F24) keyDown.TrySetResult(); };
        surface.PreviewKeyUp += (_, args) => { if (args.Key == Key.F24) keyUp.TrySetResult(); };
        GetCursorPos(out var original);
        try
        {
            window.Show();
            window.Activate();
            surface.Focus();
            var point = surface.PointToScreen(new Point(40, 40));
            Assert.True(SetCursorPos((int)point.X, (int)point.Y));
            await Task.Delay(100);
            var automation = new WindowsInputAutomation();
            using var cancelHold = new CancellationTokenSource();
            var mouseHold = automation.HoldAsync(AutomationInput.MouseRight, TimeSpan.FromSeconds(10), cancelHold.Token);
            await rightDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await cancelHold.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mouseHold);
            await rightUp.Task.WaitAsync(TimeSpan.FromSeconds(2));

            surface.Focus();
            await automation.HoldAsync(AutomationInput.Keyboard(0x87), TimeSpan.FromSeconds(0.1), CancellationToken.None);
            await Task.WhenAll(keyDown.Task, keyUp.Task).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            SetCursorPos(original.X, original.Y);
            window.Close();
        }
    });

    [Fact]
    public void AudioVoiceCreatesPlaysSilentlyAndReleases()
    {
        using var voice = new AudioService().CreateVoice();
        voice.Play(SoundCue.Tick, 0);
        voice.Play(SoundCue.Alarm, 0);
        voice.Stop();
    }

    private static void AssertBlue(CapturedFrame frame)
    {
        Assert.Equal(frame.Width * frame.Height * 4, frame.Pixels.Length);
        var offset = frame.Stride * (frame.Height / 2) + frame.Width / 2 * 4;
        var bytes = frame.Pixels.Span;
        Assert.True(bytes[offset] > 180 && bytes[offset + 2] < 60, $"Expected blue target, got B={bytes[offset]} R={bytes[offset + 2]}");
    }

    [Fact]
    public async Task SettingsWritesFinishAtShutdownAndKeepLatestValue()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RF4Overlay-test-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await using (var store = new SettingsStore(path))
            {
                Assert.Equal(120, store.Load().Bpm);
                for (var bpm = 120; bpm <= 180; bpm++)
                    store.Save(UserSettings.Default with { Bpm = bpm, PeriodSeconds = 60d / bpm });
            }
            await using var loaded = new SettingsStore(path);
            Assert.Equal(180, loaded.Load().Bpm);
            Assert.Equal(60d / 180, loaded.Load().EffectivePeriodSeconds, 10);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CaptureMonitorDoesNotTrustStreamingTitleAndDisposesTwice()
    {
        var monitor = new CaptureMonitor(new StreamingLocator());
        var reported = new TaskCompletionSource<CaptureStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.StatusChanged += (_, status) => reported.TrySetResult(status);
        monitor.Start();
        Assert.Contains("제목만", (await reported.Task.WaitAsync(TimeSpan.FromSeconds(5))).Message);
        Assert.Null(monitor.LatestFrame);
        await Task.WhenAll(monitor.DisposeAsync().AsTask(), monitor.DisposeAsync().AsTask());
        Assert.Throws<ObjectDisposedException>(() => monitor.Start());
    }

    private sealed class StreamingLocator : IGameWindowLocator
    {
        public IReadOnlyList<CaptureWindow> FindWindows() => [new(123, 1, "streaming_client", "Russian Fishing 4 [Streaming]", 1920, 1080, true)];
        public bool IsCurrent(CaptureWindow window) => true;
    }

    private static Task OnDesktop(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); done.TrySetResult(); }
                catch (Exception error) { done.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
