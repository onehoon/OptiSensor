using DrawingSystemIcons = System.Drawing.SystemIcons;
using Forms = System.Windows.Forms;

namespace OptiSensor;

internal sealed class TrayIconService : IDisposable
{
    private const int MaxTooltipLength = 63;

    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = DrawingSystemIcons.Application,
            Text = "OptiSensor",
            Visible = true,
            ContextMenuStrip = BuildMenu(showWindow, exitApplication)
        };

        _notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void UpdateTooltip(string? lastOverlayLine)
    {
        var tooltip = string.IsNullOrWhiteSpace(lastOverlayLine)
            ? "OptiSensor"
            : $"OptiSensor{Environment.NewLine}{lastOverlayLine}";

        _notifyIcon.Text = tooltip.Length <= MaxTooltipLength
            ? tooltip
            : tooltip[..(MaxTooltipLength - 1)];
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static Forms.ContextMenuStrip BuildMenu(Action showWindow, Action exitApplication)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => showWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exitApplication());
        return menu;
    }
}
