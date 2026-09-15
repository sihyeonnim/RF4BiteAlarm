using System.Drawing;
using System.Windows.Forms;
using RF4Overlay.Core.Features;

namespace RF4Overlay.Infrastructure.Tray;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Dictionary<FeatureId, ToolStripMenuItem> _items = [];
    private readonly FeatureCommandDispatcher _runtime;
    private readonly Action<Action> _dispatch;
    private bool _disposed;
    public TrayService(FeatureCommandDispatcher runtime, Action<Action> dispatch, Action show, Action exit)
    {
        _runtime = runtime; _dispatch = dispatch;
        _menu.Items.Add("RF4 Overlay 열기", null, (_, _) => show());
        _menu.Items.Add(new ToolStripSeparator());
        foreach (var status in runtime.GetStatuses())
        {
            var id = status.Id;
            var item = new ToolStripMenuItem(status.Name);
            item.Click += async (_, _) => await runtime.ExecuteAsync(new(id, FeatureAction.Toggle));
            _items.Add(id, item);
            _menu.Items.Add(item);
            Update(status);
        }
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("종료", null, (_, _) => exit());
        _icon = new NotifyIcon { Text = "RF4 Overlay", Icon = SystemIcons.Application, ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => show();
        runtime.StatusChanged += OnStatus;
    }
    private void OnStatus(object? sender, FeatureStatus status) => _dispatch(() => { if (!_disposed) Update(status); });
    private void Update(FeatureStatus status)
    {
        var item = _items[status.Id];
        item.Enabled = status.State is not (FeatureState.Unavailable or FeatureState.Stopping);
        item.Checked = status.State == FeatureState.Running;
        item.Text = status.Name + (status.State == FeatureState.Faulted ? " · 오류" : "");
        item.ToolTipText = status.Error ?? status.Description;
    }
    public void Dispose()
    {
        _disposed = true;
        _runtime.StatusChanged -= OnStatus;
        _icon.Visible = false; _icon.Dispose(); _menu.Dispose();
    }
}
