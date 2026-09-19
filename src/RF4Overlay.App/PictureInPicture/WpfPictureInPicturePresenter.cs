using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RF4Overlay.Core.Capture;

namespace RF4Overlay.App.PictureInPicture;

public sealed class WpfPictureInPicturePresenter(Dispatcher dispatcher) : IPictureInPicturePresenter
{
    public async Task ShowAsync(ICaptureFrameSource frames, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var view = await dispatcher.InvokeAsync(() => CreateWindow(lifetime), DispatcherPriority.Normal, cancellationToken);
        try
        {
            await foreach (var frame in frames.ReadFramesAsync(lifetime.Token).ConfigureAwait(false))
            {
                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32,
                    null, frame.Pixels.ToArray(), frame.Stride);
                bitmap.Freeze();
                await dispatcher.InvokeAsync(() =>
                {
                    if (!view.Window.IsVisible) return;
                    view.Image.Source = bitmap;
                    view.Message.Visibility = Visibility.Collapsed;
                }, DispatcherPriority.Render, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (view.Window.IsVisible) view.Window.Close();
            }, DispatcherPriority.Normal);
        }
    }

    private static PipView CreateWindow(CancellationTokenSource lifetime)
    {
        var image = new Image { Stretch = Stretch.Uniform };
        var message = new TextBlock
        {
            Text = "RF4 프레임 대기 중",
            Foreground = Brushes.White,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid { Background = Brushes.Black };
        content.Children.Add(image);
        content.Children.Add(message);
        var window = new Window
        {
            Title = "RF4 PIP",
            Width = 480,
            Height = 300,
            MinWidth = 240,
            MinHeight = 160,
            Content = content,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            Background = Brushes.Black
        };
        window.Closed += (_, _) => lifetime.Cancel();
        window.Show();
        return new(window, image, message);
    }

    private sealed record PipView(Window Window, Image Image, TextBlock Message);
}
