using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MessageHub;

// The notification-area icon: open or quit, and Windows notifications while the hub is hidden.
sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;

    public event Action? OpenRequested;
    public event Action? NotificationClicked;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Message Hub", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Quit", null, (_, _) => MainWindow.Quit());

        _icon = new NotifyIcon
        {
            Text = "Message Hub",
            Icon = DrawIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
        _icon.BalloonTipClicked += (_, _) => NotificationClicked?.Invoke();
    }

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, string.IsNullOrWhiteSpace(text) ? " " : text, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    // A rounded square with a speech bubble, drawn so there's no icon file to ship.
    static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var square = Rounded(new RectangleF(1, 1, 30, 30), 7);
            using var fill = new SolidBrush(Color.FromArgb(0x7B, 0x83, 0xEB));
            g.FillPath(fill, square);
            using var bubble = Rounded(new RectangleF(7, 8, 18, 13), 4);
            g.FillPath(Brushes.White, bubble);
            g.FillPolygon(Brushes.White, new[] { new PointF(11, 20), new PointF(11, 25), new PointF(16, 20) });
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
