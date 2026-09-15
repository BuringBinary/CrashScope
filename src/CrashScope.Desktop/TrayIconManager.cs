using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Timer = System.Windows.Forms.Timer;

namespace CrashScope.Desktop;

public sealed class TrayIconManager : IDisposable
{
    private readonly AppService _service;
    private readonly Window _window;
    private readonly NotifyIcon _notifyIcon;
    private readonly Timer _blinkTimer;
    private readonly Icon _iconRecording;
    private readonly Icon _iconStopped;
    private readonly Icon _iconWarning;
    private bool _blinkOn;

    public TrayIconManager(Window window, AppService service)
    {
        _service = service;
        _window = window;

        _iconRecording = CreateIcon(Color.FromArgb(0, 214, 143));
        _iconStopped = CreateIcon(Color.FromArgb(255, 107, 107));
        _iconWarning = CreateIcon(Color.FromArgb(255, 217, 61));

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconStopped,
            Text = "CrashScope - Stopped",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.DoubleClick += OnTrayDoubleClick;

        _blinkTimer = new Timer { Interval = 800 };
        _blinkTimer.Tick += OnBlinkTick;

        _service.StatusChanged += OnStatusChanged;
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start Recording", null, (_, _) => _service.StartRecording());
        menu.Items.Add("Stop Recording", null, (_, _) => _service.StopRecording());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Data Folder", null, (_, _) =>
        {
            var path = _service.RootDirectory;
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
        });
        menu.Items.Add("Show CrashScope", null, OnShowFromTray);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        menu.Opening += (_, _) =>
        {
            menu.Items[0].Enabled = !_service.IsRecording;
            menu.Items[1].Enabled = _service.IsRecording;
        };

        return menu;
    }

    private void OnStatusChanged()
    {
        _notifyIcon.Icon = _service.IsRecording ? _iconRecording : _iconStopped;
        _notifyIcon.Text = _service.IsRecording
            ? $"CrashScope - Recording ({_service.SampleCount} samples)"
            : "CrashScope - Stopped";
    }

    public void ShowUncleanSessionBalloon(string mode, double confidence)
    {
        _notifyIcon.Icon = _iconWarning;
        _notifyIcon.ShowBalloonTip(
            5000,
            "CrashScope",
            $"Previous unclean shutdown detected.\nTop cause: {mode} ({confidence:P0} confidence)\nClick to view incident.",
            ToolTipIcon.Warning);
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            OnShowFromTray(this, EventArgs.Empty);
        };
        _blinkTimer.Start();
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _blinkOn = !_blinkOn;
        _notifyIcon.Icon = _blinkOn ? _iconWarning : _iconStopped;
    }

    public void StopBlink()
    {
        _blinkTimer.Stop();
        OnStatusChanged();
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e) => OnShowFromTray(sender, e);

    private void OnShowFromTray(object? sender, EventArgs e)
    {
        StopBlink();
        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _service.StopRecording();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        _blinkTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconRecording.Dispose();
        _iconStopped.Dispose();
        _iconWarning.Dispose();
    }

    private static Icon CreateIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Background circle
        using var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 40));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Colored status dot
        using var statusBrush = new SolidBrush(color);
        g.FillEllipse(statusBrush, 8, 8, 16, 16);

        // Radar ring
        using var ringPen = new Pen(color, 2);
        g.DrawEllipse(ringPen, 3, 3, 26, 26);

        // Crosshair lines
        using var crossPen = new Pen(Color.FromArgb(80, color.R, color.G, color.B), 1);
        g.DrawLine(crossPen, 16, 6, 16, 10);
        g.DrawLine(crossPen, 16, 22, 16, 26);
        g.DrawLine(crossPen, 6, 16, 10, 16);
        g.DrawLine(crossPen, 22, 16, 26, 16);

        var hIcon = bitmap.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        return (Icon)icon.Clone();
    }
}