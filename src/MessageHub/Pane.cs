using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace MessageHub;

// One app: a slim header (name, unread count, latest message, buttons) over a WebView2.
public sealed class Pane : Border
{
    public const double HeaderHeight = 30;
    const double Edge = 2;

    static readonly Color HeaderColor = Color.FromRgb(0x25, 0x25, 0x26);
    static readonly Regex TitleCount = new(@"\((\d+)\+?\)", RegexOptions.Compiled);

    // Sign-in pages stay inside the app as popups; every other new-window link opens in the browser.
    static readonly string[] SignInHosts =
    {
        "login.microsoftonline.com", "login.microsoft.com", "login.live.com",
        "accounts.google.com", "appleid.apple.com", "okta.com",
    };

    PaneConfig _config;
    readonly MainWindow _host;
    readonly HubConfig _hub;
    readonly HubState _state;
    readonly Color _accent;
    readonly SolidColorBrush _headerBrush = new(HeaderColor);
    readonly Border _badge;
    readonly TextBlock _badgeText;
    readonly TextBlock _preview;
    readonly TextBlock _expandButton;
    bool _pageFocused;
    bool _hasLatest;

    public WebView2 Web { get; } = new();

    // Identifies the app across layouts; layouts that share an app share its pane.
    public string Key { get; }
    public PaneConfig Config => _config;
    public string LatestText => _preview.Text;
    public string Title => _config.Name;
    public int Unread { get; private set; }
    public bool Alerting { get; private set; }

    public event Action<Pane, bool>? Alerted;
    public event Action<Pane>? ExpandRequested;
    public event Action? StateChanged;

    // A null config means "ask for a custom app".
    public event Action<Pane, PaneConfig?>? SwitchRequested;
    public event Action<Pane, PaneConfig?>? AddRequested;
    public event Action<Pane, int>? MoveRequested;
    public event Action<Pane>? RemoveRequested;

    public Pane(string key, PaneConfig config, MainWindow host)
    {
        Key = key;
        _config = config;
        _host = host;
        _hub = host.Config;
        _state = host.State;
        _accent = ParseColor(config.Color);

        BorderThickness = new Thickness(Edge);
        BorderBrush = Brushes.Transparent;

        _badgeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _badge = new Border
        {
            Background = new SolidColorBrush(_accent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 0, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _badgeText,
        };
        _preview = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            FontSize = 12,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _expandButton = IconButton("", "Expand (double-click the header)", () => ExpandRequested?.Invoke(this));

        var header = new Grid { Height = HeaderHeight, Background = _headerBrush, Cursor = Cursors.Hand };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = config.Name,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var menu = new ContextMenu();
        FillMenu(menu);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(IconButton("\uE712", "Apps and options", () =>
        {
            FillMenu(menu);
            menu.PlacementTarget = header;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }));
        buttons.Children.Add(IconButton("", "Reload", () => Web.CoreWebView2?.Reload()));
        buttons.Children.Add(_expandButton);

        AddTo(header, new Border { Background = new SolidColorBrush(_accent) }, 0);
        AddTo(header, name, 1);
        AddTo(header, _badge, 2);
        AddTo(header, _preview, 3);
        AddTo(header, buttons, 4);

        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ExpandRequested?.Invoke(this);
            else OpenLatest();
        };
        header.ContextMenu = menu;
        header.ContextMenuOpening += (_, _) => FillMenu(menu);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(Web);
        Child = layout;
    }

    public async Task InitAsync(CoreWebView2Environment environment, string script)
    {
        await Web.EnsureCoreWebView2Async(environment);
        var core = Web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;

        if (_state.Zoom.TryGetValue(_config.Name, out var zoom)) Web.ZoomFactor = zoom;
        Web.ZoomFactorChanged += (_, _) => _state.Zoom[_config.Name] = Web.ZoomFactor;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        core.WebMessageReceived += OnWebMessage;
        core.DocumentTitleChanged += (_, _) => UpdateUnread(core.DocumentTitle);
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
                e.State = CoreWebView2PermissionState.Allow;
        };
        core.NewWindowRequested += OnNewWindow;
        core.Navigate(_config.Url);
    }

    public void Close() => Web.Dispose();

    // Points the pane at the showing layout's settings for this app (alerts, sound, size).
    public void Bind(PaneConfig config) => _config = config;

    public void SetExpanded(bool expanded) => _expandButton.Text = expanded ? "" : "";

    public void ClearAlert()
    {
        if (!Alerting) return;
        Alerting = false;
        _headerBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _headerBrush.Color = HeaderColor;
        BorderBrush = Brushes.Transparent;
        StateChanged?.Invoke();
    }

    public void OpenLatest()
    {
        ClearAlert();
        if (_hasLatest) _ = Web.ExecuteScriptAsync("window.__hubOpenLast && window.__hubOpenLast()");
        _hasLatest = false;
        Web.Focus();
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        // The page itself can post messages too, so anything malformed is ignored.
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "notification":
                    OnNotification(
                        root.GetProperty("title").GetString() ?? "",
                        root.GetProperty("body").GetString() ?? "",
                        root.TryGetProperty("test", out var test) && test.ValueKind == JsonValueKind.True);
                    break;
                case "focus":
                    _pageFocused = root.GetProperty("value").GetBoolean();
                    if (_pageFocused) ClearAlert();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }
    }

    void OnNotification(string title, string body, bool test)
    {
        var text = string.IsNullOrWhiteSpace(body) ? title : $"{title}: {body}";
        _preview.Text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        _preview.ToolTip = text;
        _hasLatest = true;

        var vip = _hub.VipKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && text.Contains(k, StringComparison.OrdinalIgnoreCase));
        var alertOn = _config.AlertOn.ToLowerInvariant();
        var quietBecause = test ? null
            : alertOn == "none" ? "Quiet: alerts are off for this app"
            : alertOn == "vip" && !vip ? "Quiet: not a VIP"
            : _pageFocused ? "Quiet: you were looking at it"
            : _host.Snoozed ? "Quiet: do not disturb"
            : null;
        AlertLog.Add(_config.Name, text, quietBecause ?? (test ? "Test alert" : vip ? "Alerted (VIP)" : "Alerted"));
        if (quietBecause != null) return;

        if (!Alerting)
        {
            Alerting = true;
            BorderBrush = new SolidColorBrush(_accent);
            _headerBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                From = HeaderColor,
                To = _accent,
                Duration = TimeSpan.FromMilliseconds(700),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        }
        Alerted?.Invoke(this, vip);
        StateChanged?.Invoke();
    }

    // WhatsApp and Teams put "(3)" in the page title. Slack marks unread with a leading "*" or "!".
    void UpdateUnread(string title)
    {
        var match = TitleCount.Match(title);
        var count = match.Success ? int.Parse(match.Groups[1].Value)
            : title.StartsWith('*') || title.StartsWith('!') ? -1
            : 0;
        if (count == Unread) return;

        Unread = count;
        _badge.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _badgeText.Text = count switch
        {
            < 0 => "•",
            > 99 => "99+",
            _ => count.ToString(),
        };
        StateChanged?.Invoke();
    }

    void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (SignInHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
            return;

        e.Handled = true;
        if (uri.Scheme is "http" or "https" or "mailto")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // Rebuilt each time it opens so the ticks show the current settings.
    void FillMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(AppMenu("Switch to", app => SwitchRequested?.Invoke(this, app)));
        menu.Items.Add(AppMenu("Add app below", app => AddRequested?.Invoke(this, app)));
        menu.Items.Add(AppMenu("Split with", app => _host.SplitPane(this, app)));
        if (_config.SplitWithPrevious) menu.Items.Add(Item("Unsplit (own row)", () => _host.Unsplit(this)));
        menu.Items.Add(Item("Move up", () => MoveRequested?.Invoke(this, -1)));
        menu.Items.Add(Item("Move down", () => MoveRequested?.Invoke(this, 1)));
        menu.Items.Add(Item("Remove", () => RemoveRequested?.Invoke(this)));
        menu.Items.Add(new Separator());

        var alerts = new MenuItem { Header = $"Alerts for {_config.Name}" };
        foreach (var (label, value) in new[] { ("All messages", "all"), ("VIP only", "vip"), ("Off", "none") })
        {
            alerts.Items.Add(Item(label, () =>
            {
                _config.AlertOn = value;
                _host.SaveConfig();
            }, _config.AlertOn.Equals(value, StringComparison.OrdinalIgnoreCase)));
        }
        alerts.Items.Add(new Separator());
        alerts.Items.Add(Item("Send test alert", SendTestAlert));
        menu.Items.Add(alerts);

        var sound = new MenuItem { Header = $"Sound for {_config.Name}" };
        sound.Items.Add(Item("Default", () => SetSound(null), string.IsNullOrEmpty(_config.Sound)));
        sound.Items.Add(Item("None", () => SetSound(Sounds.None), _config.Sound == Sounds.None));
        sound.Items.Add(new Separator());
        foreach (var choice in Sounds.Choices)
            sound.Items.Add(Item(choice, () => SetSound(choice), _config.Sound == choice));
        sound.Items.Add(new Separator());
        sound.Items.Add(Item("Choose a file…", () =>
        {
            var dialog = new OpenFileDialog { Title = $"Sound for {_config.Name}", Filter = "Sounds (*.wav)|*.wav" };
            if (dialog.ShowDialog() == true) SetSound(dialog.FileName);
        }, Sounds.IsFile(_config.Sound)));
        menu.Items.Add(sound);

        var dnd = new MenuItem { Header = _host.Snoozed ? $"Do not disturb ({_host.SnoozeText})" : "Do not disturb" };
        dnd.Items.Add(Item("30 minutes", () => _host.Snooze(TimeSpan.FromMinutes(30))));
        dnd.Items.Add(Item("1 hour", () => _host.Snooze(TimeSpan.FromHours(1))));
        dnd.Items.Add(Item("Until I turn it off", () => _host.Snooze(null)));
        dnd.Items.Add(new Separator());
        var off = Item("Turn off", () => _host.Snooze(TimeSpan.Zero));
        off.IsEnabled = _host.Snoozed;
        dnd.Items.Add(off);
        menu.Items.Add(dnd);

        var layouts = new MenuItem { Header = $"Layout: {_hub.Current.Name}" };
        foreach (var saved in _hub.Layouts)
        {
            var target = saved;
            layouts.Items.Add(Item(saved.Name, () => _host.SwitchLayout(target), saved == _hub.Current));
        }
        layouts.Items.Add(new Separator());
        layouts.Items.Add(Item("New layout…", _host.NewLayout));
        layouts.Items.Add(Item("Rename this layout…", _host.RenameLayout));
        var delete = Item("Delete this layout", _host.DeleteLayout);
        delete.IsEnabled = _hub.Layouts.Count > 1;
        layouts.Items.Add(delete);
        menu.Items.Add(layouts);

        var arrange = new MenuItem { Header = "Arrange" };
        foreach (var (label, value) in LayoutChoices.Arrangements)
            arrange.Items.Add(Item(label, () => _host.SetArrangement(value), _hub.Current.Arrangement.Equals(value, StringComparison.OrdinalIgnoreCase)));
        arrange.Items.Add(new Separator());
        for (var count = LayoutChoices.MinApps; count <= LayoutChoices.MaxApps; count++)
        {
            var n = count;
            arrange.Items.Add(Item($"{n} apps", () => _host.SetPaneCount(n), _host.PaneCount == n));
        }
        menu.Items.Add(arrange);

        var settings = new MenuItem { Header = "Settings" };
        settings.Items.Add(Item("VIP list…", _host.EditVipList));
        settings.Items.Add(Toggle("Sound", () => _hub.Sound, v => _hub.Sound = v));
        settings.Items.Add(Toggle("Flash taskbar", () => _hub.FlashTaskbar, v => _hub.FlashTaskbar = v));
        settings.Items.Add(Toggle("Expand pane for VIP messages", () => _hub.ExpandOnVip, v => _hub.ExpandOnVip = v));
        settings.Items.Add(new Separator());
        settings.Items.Add(Item("Start with Windows", () => StartWithWindows.Enabled = !StartWithWindows.Enabled, StartWithWindows.Enabled));
        settings.Items.Add(Toggle("Keep running when closed (minimise)", () => _hub.CloseToTray, v => _hub.CloseToTray = v));
        settings.Items.Add(new Separator());
        settings.Items.Add(Item("Recent alerts…", _host.ShowAlertLog));
        settings.Items.Add(Item("Check for updates", () => _ = _host.CheckForUpdatesAsync(manual: true)));
        settings.Items.Add(Item("Restart Message Hub", MainWindow.Restart));
        settings.Items.Add(Item("Quit Message Hub", MainWindow.Quit));
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Reload", () => Web.CoreWebView2?.Reload()));
        menu.Items.Add(Item("Expand / restore", () => ExpandRequested?.Invoke(this)));
        menu.Items.Add(Item(_host.IsPoppedOut(this) ? "Put back in the hub" : "Pop out to its own window", () => _host.TogglePopOut(this)));
        menu.Items.Add(Item("Reset zoom", () => Web.ZoomFactor = 1.0));
        menu.Items.Add(Item("Open in browser", () =>
            Process.Start(new ProcessStartInfo(Web.Source?.AbsoluteUri ?? _config.Url) { UseShellExecute = true })));
    }

    void SetSound(string? sound)
    {
        _config.Sound = sound;
        Sounds.Play(sound);
        _host.SaveConfig();
    }

    // Goes through the page's own notification path, so it proves alerts reach the hub from this app.
    void SendTestAlert()
    {
        var body = JsonSerializer.Serialize($"This is how a new {_config.Name} message looks");
        _ = Web.ExecuteScriptAsync($"new Notification('Test alert', {{ body: {body}, tag: '__hub_test' }})");
    }

    MenuItem Toggle(string header, Func<bool> get, Action<bool> set) => Item(header, () =>
    {
        set(!get());
        _host.SaveConfig();
    }, get());

    static MenuItem AppMenu(string header, Action<PaneConfig?> choose)
    {
        var menu = new MenuItem { Header = header };
        foreach (var app in AppCatalog.Apps)
            menu.Items.Add(Item(app.Name, () => choose(app.Copy())));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Custom…", () => choose(null)));
        return menu;
    }

    static MenuItem Item(string header, Action action, bool isChecked = false)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
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
            Padding = new Thickness(9, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
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

    static void AddTo(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (Exception)
        {
            return Colors.Gray;
        }
    }
}
