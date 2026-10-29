using System.Drawing;
using System.Reflection;
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
            Icon = LoadIcon(),
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

    // The app icon at the size Windows uses for the notification area.
    static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
