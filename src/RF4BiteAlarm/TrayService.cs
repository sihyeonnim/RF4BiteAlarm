using System.Drawing;
using System.Windows.Forms;

namespace RF4BiteAlarm;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _toggle;
    private readonly Action _show;
    private readonly Action _toggleAlarm;
    private readonly Action _exit;

    public TrayService(Action show, Action toggleAlarm, Action exit)
    {
        _show = show;
        _toggleAlarm = toggleAlarm;
        _exit = exit;
        _menu.Items.Add("RF4 Bite Alarm 열기", null, (_, _) => _show());
        _toggle = new ToolStripMenuItem("알람 시작", null, (_, _) => _toggleAlarm());
        _menu.Items.Add(_toggle);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("종료", null, (_, _) => _exit());

        var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        _icon = new NotifyIcon
        {
            Text = "RF4 Bite Alarm",
            Icon = icon,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => _show();
    }

    public void SetRunning(bool running)
    {
        _toggle.Checked = running;
        _toggle.Text = running ? "알람 정지" : "알람 시작";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
