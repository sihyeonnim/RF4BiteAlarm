using System.Windows;
using RF4Overlay.App.ViewModels;
using RF4Overlay.Core.Features;
using RF4Overlay.Features;

namespace RF4Overlay.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new FeatureCommandDispatcher(FeatureCatalog.Create()));
    }
}
