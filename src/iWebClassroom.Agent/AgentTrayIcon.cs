using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using IWebClassroom.Agent.Localization;
using Forms = System.Windows.Forms;

namespace IWebClassroom.Agent;

internal sealed class AgentTrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _assignmentItem;
    private readonly Forms.ToolStripMenuItem _computerItem;
    private readonly Forms.ToolStripMenuItem _serverItem;
    private readonly Forms.Timer _timer;
    private readonly Icon _icon;
    private readonly IntPtr _iconHandle;
    private bool _disposed;

    public AgentTrayIcon(MainWindow window)
    {
        _window = window;
        (_icon, _iconHandle) = CreatePortalIcon();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Item(AgentStrings.OpenAgent, (_, _) => ShowWindow(), bold: true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _statusItem = InformationItem(string.Empty);
        _assignmentItem = InformationItem(string.Empty);
        _computerItem = InformationItem(string.Empty);
        _serverItem = InformationItem(string.Empty);
        menu.Items.Add(_statusItem);
        menu.Items.Add(_assignmentItem);
        menu.Items.Add(_computerItem);
        menu.Items.Add(_serverItem);
        menu.Items.Add(InformationItem($"{AgentStrings.TrayVersion}: {Assembly.GetExecutingAssembly().GetName().Version}"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item(AgentStrings.CopyServerAddress, (_, _) => CopyServerAddress()));
        menu.Items.Add(Item(AgentStrings.RefreshInformation, async (_, _) => await RefreshAsync()));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "iWebClassroom Agent",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        _timer = new Forms.Timer { Interval = 10_000 };
        _timer.Tick += (_, _) => RefreshMenu();
        _timer.Start();
        RefreshMenu();
    }

    public void NotifyStillRunning()
    {
        _notifyIcon.BalloonTipTitle = "iWebClassroom";
        _notifyIcon.BalloonTipText = AgentStrings.AgentKeepsRunning;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ShowWindow() => _window.Dispatcher.Invoke(_window.ShowFromTray);

    private async Task RefreshAsync()
    {
        await _window.Dispatcher.InvokeAsync(_window.RefreshFromTrayAsync).Task.Unwrap();
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        if (_disposed)
        {
            return;
        }

        _window.Dispatcher.Invoke(() =>
        {
            _statusItem.Text = $"{AgentStrings.TrayStatus}: {_window.TrayStatus}";
            _assignmentItem.Text = $"{AgentStrings.TrayAssignment}: {_window.TrayAssignment}";
            _computerItem.Text = $"{AgentStrings.TrayComputer}: {_window.TrayComputer}";
            _serverItem.Text = $"{AgentStrings.TrayServer}: {_window.TrayServer}";
            var tooltip = $"iWebClassroom Agent · {_window.TrayStatus}";
            _notifyIcon.Text = tooltip[..Math.Min(63, tooltip.Length)];
        });
    }

    private void CopyServerAddress()
    {
        try
        {
            Forms.Clipboard.SetText(_window.TrayServer);
        }
        catch (ExternalException)
        {
            // Clipboard may temporarily be owned by another process.
        }
    }

    private static Forms.ToolStripMenuItem InformationItem(string text)
    {
        var item = new Forms.ToolStripMenuItem(text) { Enabled = false };
        return item;
    }

    private static Forms.ToolStripMenuItem Item(string text, EventHandler handler, bool bold = false)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += handler;
        if (bold)
        {
            item.Font = new Font(item.Font, System.Drawing.FontStyle.Bold);
        }
        return item;
    }

    private static (Icon Icon, IntPtr Handle) CreatePortalIcon()
    {
        var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var darkBrush = new SolidBrush(Color.FromArgb(7, 22, 45)))
        using (var mintBrush = new SolidBrush(Color.FromArgb(86, 227, 192)))
        using (var checkPen = new Pen(Color.FromArgb(86, 227, 192), 4f))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = RoundedRectangle(new RectangleF(0, 0, 64, 64), 16);
            graphics.FillPath(darkBrush, background);
            graphics.FillPolygon(mintBrush, [new PointF(12, 18), new PointF(32, 9), new PointF(52, 18), new PointF(52, 46), new PointF(32, 56), new PointF(12, 46)]);
            graphics.FillPolygon(darkBrush, [new PointF(21, 25), new PointF(32, 20), new PointF(43, 25), new PointF(43, 40), new PointF(32, 45), new PointF(21, 40)]);
            checkPen.StartCap = LineCap.Round;
            checkPen.EndCap = LineCap.Round;
            checkPen.LineJoin = LineJoin.Round;
            graphics.DrawLines(checkPen, [new PointF(26, 32), new PointF(30, 36), new PointF(38, 27)]);
        }

        var handle = bitmap.GetHicon();
        bitmap.Dispose();
        return (Icon.FromHandle(handle), handle);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
        DestroyIcon(_iconHandle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
