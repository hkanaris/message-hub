using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MessageHub;

// One message on the feed: what an app's notification said, and when.
public sealed class FeedItem
{
    public required DateTime Time { get; init; }
    public required Pane Pane { get; init; }
    public required string App { get; init; }
    public required Color Color { get; init; }
    public required string Sender { get; init; }
    public required string Message { get; init; }

    // Matches Pane.LatestSerial while this is the newest notification the pane can still open.
    public required int Serial { get; init; }
    public bool Read { get; set; }
}

// The message board: every app's notifications in one list, newest first. Docked beside the
// panes or popped out into its own window.
public sealed class FeedView : DockPanel
{
    const int Limit = 300;
    const string AllApps = "All apps";

    static readonly Brush Unread = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x3A));
    static readonly Brush Hover = new SolidColorBrush(Color.FromRgb(0x33, 0x36, 0x44));
    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA0));
    static readonly Brush Body = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    readonly List<FeedItem> _items = new();
    readonly StackPanel _list = new();
    readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _filter = new() { Margin = new Thickness(10, 0, 10, 8) };
    readonly TextBlock _empty = new()
    {
        Text = "New messages from every app will appear here, newest first.",
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(12),
    };
    readonly TextBlock _popButton;
    readonly TextBlock _pinButton;

    public event Action<FeedItem>? ItemClicked;
    public event Action? PopOutRequested;
    public event Action? HideRequested;
    public event Action? PinRequested;
    public event Action? UnreadChanged;

    public int UnreadCount => _items.Count(i => !i.Read);

    public FeedView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22));
        MinWidth = 220;

        _popButton = IconButton("", "Pop out to its own window", () => PopOutRequested?.Invoke());
        _pinButton = IconButton("", "Keep on top", () => PinRequested?.Invoke());
        _pinButton.Visibility = Visibility.Collapsed;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(IconButton("", "Mark all as read", MarkAllRead));
        buttons.Children.Add(_pinButton);
        buttons.Children.Add(_popButton);
        buttons.Children.Add(IconButton("", "Hide the feed (Ctrl+Shift+F)", () => HideRequested?.Invoke()));

        var header = new DockPanel { Margin = new Thickness(12, 8, 4, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(_title);

        _filter.Items.Add(AllApps);
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => Render();

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };

        SetDock(header, Dock.Top);
        SetDock(_filter, Dock.Top);
        SetDock(_empty, Dock.Top);
        Children.Add(header);
        Children.Add(_filter);
        Children.Add(_empty);
        Children.Add(scroll);
        Render();
    }

    public void SetPoppedOut(bool poppedOut, bool onTop)
    {
        _popButton.Text = poppedOut ? "" : "";
        _popButton.ToolTip = poppedOut ? "Put back beside the panes" : "Pop out to its own window";
        _pinButton.Visibility = poppedOut ? Visibility.Visible : Visibility.Collapsed;
        _pinButton.Opacity = onTop ? 1 : 0.6;
        _pinButton.ToolTip = onTop ? "Stop keeping on top" : "Keep on top";
    }

    public void Add(FeedItem item)
    {
        _items.Insert(0, item);
        if (_items.Count > Limit) _items.RemoveAt(_items.Count - 1);
        if (!_filter.Items.Contains(item.App)) _filter.Items.Add(item.App);
        Render();
        UnreadChanged?.Invoke();
    }

    // Everything from that pane counts as read once you've looked at it.
    public void MarkRead(Pane pane)
    {
        var changed = false;
        foreach (var item in _items.Where(i => i.Pane == pane && !i.Read))
        {
            item.Read = true;
            changed = true;
        }
        if (!changed) return;
        Render();
        UnreadChanged?.Invoke();
    }

    void MarkAllRead()
    {
        foreach (var item in _items) item.Read = true;
        Render();
        UnreadChanged?.Invoke();
    }

    void Render()
    {
        var app = _filter.SelectedItem as string ?? AllApps;
        var shown = app == AllApps ? _items : _items.Where(i => i.App == app).ToList();

        _list.Children.Clear();
        foreach (var item in shown) _list.Children.Add(Entry(item));

        var unread = UnreadCount;
        _title.Text = unread > 0 ? $"Feed ({unread} new)" : "Feed";
        _empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    UIElement Entry(FeedItem item)
    {
        var accent = new SolidColorBrush(item.Color);
        var app = new TextBlock { Text = item.App, Foreground = accent, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var time = new TextBlock
        {
            Text = item.Time.Date == DateTime.Today ? item.Time.ToString("HH:mm") : item.Time.ToString("ddd HH:mm"),
            Foreground = Muted,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var top = new DockPanel();
        DockPanel.SetDock(time, Dock.Right);
        top.Children.Add(time);
        top.Children.Add(app);

        var text = new StackPanel();
        text.Children.Add(top);
        if (item.Sender.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Sender,
                Foreground = Brushes.White,
                FontWeight = item.Read ? FontWeights.Normal : FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        if (item.Message.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Message,
                Foreground = Body,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 52,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var background = item.Read ? Brushes.Transparent : Unread;
        var entry = new Border
        {
            Background = background,
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
            ToolTip = item.Sender.Length > 0 ? $"{item.Sender}: {item.Message}" : item.Message,
            Child = text,
        };
        entry.MouseEnter += (_, _) => entry.Background = Hover;
        entry.MouseLeave += (_, _) => entry.Background = background;
        entry.MouseLeftButtonUp += (_, _) =>
        {
            item.Read = true;
            ItemClicked?.Invoke(item);
            Render();
            UnreadChanged?.Invoke();
        };
        return entry;
    }

    static TextBlock IconButton(string glyph, string tooltip, Action action)
    {
        var button = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = Brushes.White,
            Opacity = 0.6,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
        };
        button.MouseEnter += (_, _) => button.Opacity = 1;
        button.MouseLeave += (_, _) => button.Opacity = 0.6;
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }
}

// The feed in its own window. Closing it docks the feed back beside the panes.
public sealed class FeedWindow : Window
{
    bool _quiet;

    public event Action? Returned;

    public FeedWindow(FeedView feed, bool onTop)
    {
        Title = "Message feed · Message Hub";
        Width = 380;
        Height = 700;
        Topmost = onTop;
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22));
        Content = feed;
        Closing += (_, _) =>
        {
            Content = null;
            if (!_quiet && !MainWindow.Quitting) Returned?.Invoke();
        };
    }

    public void CloseQuietly()
    {
        _quiet = true;
        Close();
    }
}
