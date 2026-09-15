using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RF4Overlay.Core.Audio;
using RF4Overlay.Core.Capture;
using RF4Overlay.Infrastructure.Audio;
using RF4Overlay.Infrastructure.Capture;
using RF4Overlay.Infrastructure.Input;

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
        await using var capture = new WgcWindowCapture();
        try
        {
            target.Show();
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
        finally { cover.Close(); target.Close(); }
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
}
