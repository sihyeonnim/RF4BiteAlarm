using System.Windows;
using RF4Overlay.App.ViewModels;
using RF4Overlay.Core.Features;
using RF4Overlay.Features;

namespace RF4Overlay.App;

public partial class MainWindow : Window
{
    private readonly FeatureCommandDispatcher _runtime = new(FeatureCatalog.Create());
    private bool _closing;
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(_runtime);
        Closing += async (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true;
            IsEnabled = false;
            await _runtime.DisposeAsync();
            ((MainViewModel)DataContext).Dispose();
            _closing = true;
            Close();
        };
    }
}
