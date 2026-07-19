using System.Diagnostics;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using Forms = System.Windows.Forms;

namespace OptiSensor.UI;

internal sealed class TrayIconService : IDisposable
{
    private const int MaxTooltipLength = 63;

    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "OptiSensor",
            Visible = true,
            ContextMenuStrip = BuildMenu(showWindow, exitApplication)
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                showWindow();
        };
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

    private static DrawingIcon LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var icon = DrawingIcon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
                return icon;
        }

        return DrawingSystemIcons.Application;
    }
}
