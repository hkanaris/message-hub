using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MessageHub;

// One message waiting on the feed: what an app's notification said, and when.
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

    // The app's id for the notification, so it can be dropped when the app withdraws it, and its tag:
    // apps give each conversation one tag, so a newer message replaces the older entry.
    public int? NoticeId { get; init; }
    public string? Tag { get; init; }
}

// The message board: messages from every app that still need attention, newest first. An entry
// goes when the app withdraws its notification (read or replied to), when a newer message in the
// same conversation replaces it, when the app's unread count reaches zero, or when it's clicked
// or dismissed. Pops up over the panes, or lives in its own window.
public sealed class FeedView : DockPanel
{
    const int Limit = 300;
    const string AllApps = "All apps";

    static readonly Brush Normal = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x3A));
    static readonly Brush Hover = new SolidColorBrush(Color.FromRgb(0x33, 0x36, 0x44));
    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA0));
    static readonly Brush Body = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    readonly List<FeedItem> _items = new();
    readonly StackPanel _list = new();
    readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _filter = new() { Margin = new Thickness(10, 0, 10, 8) };
    readonly TextBlock _empty = new()
    {
        Text = "Nothing waiting. New messages from every app appear here until they're read.",
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
    public int Count => _items.Count;

    public FeedView()
    {
        Background = Brushes.Transparent;
        MinWidth = 220;

        _popButton = IconButton("", "Pop out to its own window", () => PopOutRequested?.Invoke());
        _pinButton = IconButton("", "Keep on top", () => PinRequested?.Invoke());
        _pinButton.Visibility = Visibility.Collapsed;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(IconButton("\uE74D", "Clear all", ClearAll));
        buttons.Children.Add(_pinButton);
        buttons.Children.Add(_popButton);
        buttons.Children.Add(IconButton("", "Close", () => HideRequested?.Invoke()));

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
        _popButton.ToolTip = poppedOut ? "Put back in the hub" : "Pop out to its own window";
        _pinButton.Visibility = poppedOut ? Visibility.Visible : Visibility.Collapsed;
        _pinButton.Opacity = onTop ? 1 : 0.6;
        _pinButton.ToolTip = onTop ? "Stop keeping on top" : "Keep on top";
    }

    public void Add(FeedItem item)
    {
        if (item.Tag != null) _items.RemoveAll(i => i.Pane == item.Pane && i.Tag == item.Tag);
        _items.Insert(0, item);
        if (_items.Count > Limit) _items.RemoveAt(_items.Count - 1);
        if (!_filter.Items.Contains(item.App)) _filter.Items.Add(item.App);
        Render();
    }

    // The app withdrew this notification, usually because the message was read.
    public void Remove(Pane pane, int noticeId)
    {
        if (_items.RemoveAll(i => i.Pane == pane && i.NoticeId == noticeId) > 0) Render();
    }

    // Nothing unread in that app any more, or the app was closed.
    public void Clear(Pane pane)
    {
        if (_items.RemoveAll(i => i.Pane == pane) > 0) Render();
    }

    void ClearAll()
    {
        _items.Clear();
        Render();
    }

    void Dismiss(FeedItem item)
    {
        _items.Remove(item);
        Render();
    }

    void Render()
    {
        var app = _filter.SelectedItem as string ?? AllApps;
        var shown = app == AllApps ? _items : _items.Where(i => i.App == app).ToList();

        _list.Children.Clear();
        foreach (var item in shown) _list.Children.Add(Entry(item));

        _title.Text = _items.Count > 0 ? $"Feed ({_items.Count})" : "Feed";
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
        var dismiss = new TextBlock
        {
            Text = "\uE711",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = Muted,
            Margin = new Thickness(8, 1, 0, 0),
            ToolTip = "Dismiss",
            Visibility = Visibility.Hidden,
        };
        dismiss.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Dismiss(item);
        };

        var top = new DockPanel();
        DockPanel.SetDock(dismiss, Dock.Right);
        DockPanel.SetDock(time, Dock.Right);
        top.Children.Add(dismiss);
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
                FontWeight = FontWeights.SemiBold,
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

        var background = Normal;
        var entry = new Border
        {
            Background = background,
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
            ToolTip = item.Sender.Length > 0 && item.Message.Length > 0 ? $"{item.Sender}: {item.Message}" : item.Sender + item.Message,
            Child = text,
        };
        entry.MouseEnter += (_, _) =>
        {
            entry.Background = Hover;
            dismiss.Visibility = Visibility.Visible;
        };
        entry.MouseLeave += (_, _) =>
        {
            entry.Background = background;
            dismiss.Visibility = Visibility.Hidden;
        };
        entry.MouseLeftButtonUp += (_, _) =>
        {
            _items.Remove(item);
            ItemClicked?.Invoke(item);
            Render();
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

// The feed in its own window. Closing it puts the feed back as the hub's pop-up.
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
