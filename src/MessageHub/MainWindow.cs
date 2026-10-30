using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace MessageHub;

// Shows the active layout's apps stacked, side by side or in a grid. Every app in every layout
// stays running, so the ones in layouts that aren't showing still alert. One pane can be expanded
// (the rest shrink to their headers) and any pane can be popped out into its own window.
public sealed class MainWindow : Window
{
    public static bool Quitting { get; private set; }

    static readonly Color AlertRed = Color.FromRgb(0xE0, 0x1E, 0x2E);

    readonly HubConfig _config;
    readonly HubState _state;
    readonly bool _startHidden;

    // The window's state before it was last minimised, so restoring brings back a maximised window.
    WindowState _lastShownState = WindowState.Normal;
    readonly Grid _grid = new();
    readonly Border _alertBar;
    readonly TextBlock _alertText;
    readonly TrayIcon _tray = new();

    // The message feed: a panel that pops up over the panes on demand, or its own window.
    readonly FeedView _feed = new();
    readonly Border _feedFrame;
    readonly Popup _feedPopup;
    FeedWindow? _feedWindow;
    DateTime _feedClosedAt;

    // One pane per app across all layouts, keyed by Pane.Key.
    readonly Dictionary<string, Pane> _pool = new();

    // The showing layout's panes, in order.
    readonly List<Pane> _panes = new();
    readonly Dictionary<Pane, PopOutWindow> _poppedOut = new();

    // Grids holding split rows, keyed by their panes, reused so those panes stay put.
    readonly Dictionary<string, Grid> _splitGrids = new();

    readonly TaskCompletionSource<CoreWebView2Environment> _environment = new();
    readonly string _script = ReadScript();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    Pane? _expanded;
    Pane? _notifiedPane;
    AlertLogWindow? _alertLog;
    DateTime _lastSound = DateTime.MinValue;
    DateTime _nextUpdateCheck = DateTime.Now.AddSeconds(20);
    Version? _declinedUpdate;
    bool _updating;
    bool _checkingForUpdates;

    public HubConfig Config => _config;
    public HubState State => _state;
    public int PaneCount => _panes.Count;

    public bool Snoozed => _state.SnoozedUntil is { } until && until > DateTime.Now;

    public string SnoozeText => _state.SnoozedUntil == DateTime.MaxValue
        ? "until turned off"
        : $"until {_state.SnoozedUntil:HH:mm}";

    public MainWindow(HubConfig config, HubState state, bool startHidden)
    {
        _config = config;
        _state = state;
        _startHidden = startHidden;

        Title = "Message Hub";
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1C));
        TaskbarItemInfo = new TaskbarItemInfo();
        ApplySavedBounds(state);

        _alertText = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 5, 12, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _alertBar = new Border
        {
            Background = new SolidColorBrush(AlertRed),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Child = _alertText,
            ToolTip = "Click to open",
        };
        _alertBar.MouseLeftButtonDown += (_, _) =>
        {
            var pane = _pool.Values.FirstOrDefault(p => p.Alerting && !_panes.Contains(p));
            if (pane != null) ShowPane(pane);
        };

        var layout = new DockPanel();
        DockPanel.SetDock(_alertBar, Dock.Top);
        layout.Children.Add(_alertBar);
        layout.Children.Add(_grid);

        Content = layout;

        _feedFrame = new Border
        {
            Width = 380,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4A)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22)),
            Margin = new Thickness(0, 4, 12, 12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5 },
            Child = _feed,
        };
        _feedPopup = new Popup
        {
            Child = _feedFrame,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        Deactivated += (_, _) => _feedPopup.IsOpen = false;
        _feedPopup.Closed += (_, _) => _feedClosedAt = DateTime.Now;

        _feed.ItemClicked += item =>
        {
            _feedPopup.IsOpen = false;
            if (_pool.ContainsValue(item.Pane)) ShowPane(item.Pane, openLatest: item.Serial == item.Pane.LatestSerial);
        };
        _feed.HideRequested += () =>
        {
            if (_feedWindow != null) _feedWindow.Close();
            else _feedPopup.IsOpen = false;
        };
        _feed.PopOutRequested += ToggleFeedPopOut;
        _feed.PinRequested += () =>
        {
            _state.FeedOnTop = !_state.FeedOnTop;
            if (_feedWindow != null) _feedWindow.Topmost = _state.FeedOnTop;
            _feed.SetPoppedOut(_feedWindow != null, _state.FeedOnTop);
            SaveState();
        };
        _feed.SetPoppedOut(false, _state.FeedOnTop);

        _tray.OpenRequested += ShowFromTray;
        _tray.NotificationClicked += () =>
        {
            if (_notifiedPane != null) ShowPane(_notifiedPane);
            else ShowFromTray();
        };

        Sync();

        Loaded += async (_, _) =>
        {
            if (_startHidden) WindowState = WindowState.Minimized;
            await CreateEnvironmentAsync();
        };
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _lastShownState = WindowState;
        };
        PreviewKeyDown += OnKey;
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    // Every 30 seconds: end an expired do-not-disturb, and check for updates every 6 hours.
    void OnTick()
    {
        if (_state.SnoozedUntil is { } until && until <= DateTime.Now)
        {
            _state.SnoozedUntil = null;
            UpdateTaskbar();
        }

        if (DateTime.Now >= _nextUpdateCheck)
        {
            _nextUpdateCheck = DateTime.Now.AddHours(6);
            Updater.CleanUp();
            _ = CheckForUpdatesAsync(manual: false);
        }
    }

    public static void Quit()
    {
        Quitting = true;
        Application.Current.Shutdown();
    }

    public static void Restart()
    {
        var hidden = Application.Current.MainWindow is { IsVisible: false };
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, hidden ? "--restart --tray" : "--restart") { UseShellExecute = false });
        Quit();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastShownState;
        Activate();
    }

    // Opens the feed under the button that asked for it, or at the top right of the window.
    public void ShowFeed(UIElement? anchor = null)
    {
        if (_feedWindow != null)
        {
            _feedWindow.Activate();
            return;
        }
        if (_feedPopup.IsOpen)
        {
            _feedPopup.IsOpen = false;
            return;
        }

        // Clicking the button while the feed is open closes it first; don't reopen it straight away.
        if (DateTime.Now - _feedClosedAt < TimeSpan.FromMilliseconds(300)) return;

        _feedFrame.Height = Math.Max(300, Math.Min(680, ActualHeight - 80));
        if (anchor is FrameworkElement button)
        {
            _feedPopup.PlacementTarget = button;
            _feedPopup.Placement = PlacementMode.Bottom;
            _feedPopup.HorizontalOffset = button.ActualWidth - _feedFrame.Width;
            _feedPopup.VerticalOffset = 0;
        }
        else
        {
            _feedPopup.PlacementTarget = this;
            _feedPopup.Placement = PlacementMode.Relative;
            _feedPopup.HorizontalOffset = ActualWidth - _feedFrame.Width - 40;
            _feedPopup.VerticalOffset = 40;
        }
        _feedPopup.IsOpen = true;
    }

    void ToggleFeedPopOut()
    {
        if (_feedWindow != null)
        {
            _feedWindow.Close();
            return;
        }

        _feedPopup.IsOpen = false;
        _feedFrame.Child = null;
        _feedWindow = new FeedWindow(_feed, _state.FeedOnTop);
        _feedWindow.Returned += () =>
        {
            _feedWindow = null;
            _feedFrame.Child = _feed;
            _feed.SetPoppedOut(false, _state.FeedOnTop);
        };
        _feed.SetPoppedOut(true, _state.FeedOnTop);
        _feedWindow.Show();
    }

    // Brings the window up on the pane's layout and opens its latest message.
    void ShowPane(Pane pane, bool openLatest = true)
    {
        ShowFromTray();
        if (_poppedOut.TryGetValue(pane, out var window))
        {
            window.Activate();
        }
        else if (!_panes.Contains(pane))
        {
            var layout = _config.Layouts.FirstOrDefault(l => Keys(l).Any(k => k.Key == pane.Key));
            if (layout != null) SwitchLayout(layout);
        }

        if (openLatest)
        {
            pane.OpenLatest();
        }
        else
        {
            // An older message can't be opened directly; show the app so it can be found there.
            pane.ClearAlert();
            pane.Web.Focus();
        }
    }

    // A null duration means until turned off; TimeSpan.Zero turns it off.
    public void Snooze(TimeSpan? duration)
    {
        _state.SnoozedUntil = duration switch
        {
            null => DateTime.MaxValue,
            { } d when d <= TimeSpan.Zero => null,
            { } d => DateTime.Now + d,
        };
        if (Snoozed)
        {
            foreach (var pane in _pool.Values) pane.ClearAlert();
        }
        SaveState();
        UpdateTaskbar();
    }

    public void EditVipList()
    {
        var list = VipListDialog.Ask(this, _config.VipKeywords);
        if (list == null) return;
        _config.VipKeywords = list;
        SaveConfig();
    }

    public void ShowAlertLog()
    {
        if (_alertLog is { IsLoaded: true })
        {
            _alertLog.Activate();
            return;
        }
        _alertLog = new AlertLogWindow { Owner = this };
        _alertLog.Show();
    }

    public void SetArrangement(string arrangement)
    {
        _config.Current.Arrangement = arrangement;
        Arrange();
        SaveConfig();
    }

    public void SwitchLayout(LayoutConfig layout)
    {
        _config.ActiveLayout = layout.Name;
        Sync();
        SaveConfig();
    }

    public void NewLayout()
    {
        var layout = NewLayoutDialog.Ask(this, _config.Layouts.Select(l => l.Name));
        if (layout == null) return;
        _config.Layouts.Add(layout);
        SwitchLayout(layout);
    }

    public void RenameLayout()
    {
        var current = _config.Current;
        var name = PromptDialog.Ask(this, "Rename layout", "Name", current.Name);
        if (string.IsNullOrWhiteSpace(name) || name == current.Name) return;
        if (_config.Layouts.Any(l => l.Name == name))
        {
            MessageBox.Show(this, $"There's already a layout called {name}.", "Message Hub");
            return;
        }
        current.Name = name;
        _config.ActiveLayout = name;
        SaveConfig();
    }

    public void DeleteLayout()
    {
        if (_config.Layouts.Count == 1) return;
        var current = _config.Current;
        if (MessageBox.Show(this, $"Delete the {current.Name} layout?", "Message Hub", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        _config.Layouts.Remove(current);
        SwitchLayout(_config.Layouts[0]);
    }

    // Adds apps the layout doesn't have yet, or removes panes from the end, to reach the count.
    public void SetPaneCount(int count)
    {
        var panes = _config.Current.Panes;
        var enabled = Enabled();
        foreach (var extra in enabled.Skip(count)) panes.Remove(extra);
        for (var i = enabled.Count; i < count; i++)
        {
            var app = AppCatalog.Apps.FirstOrDefault(a => panes.All(p => p.Url != a.Url)) ?? AppCatalog.Apps[0];
            panes.Add(app.Copy());
        }
        Sync();
        SaveConfig();
    }

    public bool IsPoppedOut(Pane pane) => _poppedOut.ContainsKey(pane);

    public void TogglePopOut(Pane pane)
    {
        if (_poppedOut.TryGetValue(pane, out var existing))
        {
            existing.Close();
            return;
        }

        if (_expanded == pane) _expanded = null;
        Detach(pane);
        var window = new PopOutWindow(pane);
        window.Returned += w =>
        {
            _poppedOut.Remove(w.Pane);
            Arrange();
        };
        _poppedOut[pane] = window;
        window.Show();
        Arrange();
    }

    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updating || _checkingForUpdates) return;
        _checkingForUpdates = true;
        try
        {
            await CheckAndInstallAsync(manual);
        }
        finally
        {
            _checkingForUpdates = false;
        }
    }

    async Task CheckAndInstallAsync(bool manual)
    {
        if (manual) Title = "Message Hub · checking for updates…";
        try
        {
            var latest = await Updater.GetLatestAsync();
            UpdateTaskbar();
            if (latest == null || latest.Version <= Updater.Current)
            {
                if (manual) MessageBox.Show(this, $"You're up to date (v{Updater.Current}).", "Message Hub");
                return;
            }
            if (!manual && latest.Version == _declinedUpdate) return;

            var answer = MessageBox.Show(this,
                $"Message Hub {latest.Tag} is available (you have v{Updater.Current}).\n\n" +
                "Update now? It downloads in the background and restarts when it's ready.",
                "Message Hub", MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes)
            {
                _declinedUpdate = latest.Version;
                return;
            }

            _updating = true;
            Title = $"Message Hub · downloading {latest.Tag}…";
            await Updater.InstallAsync(latest);
            try
            {
                Restart();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _updating = false;
                UpdateTaskbar();
                MessageBox.Show(this,
                    $"{latest.Tag} is installed but Windows wouldn't start it. Quit Message Hub from the tray icon and start it again.",
                    "Message Hub");
            }
        }
        catch (Exception ex)
        {
            _updating = false;
            UpdateTaskbar();
            var reason = ex switch
            {
                TaskCanceledException => "GitHub didn't answer within 20 seconds. The network may be blocking api.github.com.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } =>
                    "GitHub says the releases page doesn't exist. Is the message-hub repository public?",
                _ => ex.Message,
            };
            if (manual) MessageBox.Show(this, $"Couldn't check for updates.\n\n{reason}", "Message Hub");
        }
    }

    async Task CreateEnvironmentAsync()
    {
        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MessageHub", "WebView2");
            _environment.SetResult(await CoreWebView2Environment.CreateAsync(null, dataFolder));
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Message Hub needs the Microsoft Edge WebView2 Runtime, which normally comes with Windows 11. " +
                "Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and start Message Hub again.",
                "Message Hub");
            Quit();
        }
    }

    async Task StartPaneAsync(Pane pane)
    {
        var environment = await _environment.Task;
        if (!_pool.ContainsValue(pane)) return;
        try
        {
            await pane.InitAsync(environment, _script);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{pane.Title} couldn't start.\n\n{ex.Message}", "Message Hub");
        }
    }

    static string ReadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HubScript.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Each app in a layout gets a key from its address. A second copy of the same app in one
    // layout gets its own key, so it has its own pane.
    static IEnumerable<(string Key, PaneConfig Config)> Keys(LayoutConfig layout)
    {
        var seen = new Dictionary<string, int>();
        foreach (var config in layout.Panes.Where(p => p.Enabled))
        {
            var app = $"{config.Url.Trim().TrimEnd('/').ToLowerInvariant()}|{config.Profile?.ToLowerInvariant()}";
            seen[app] = seen.GetValueOrDefault(app) + 1;
            yield return ($"{app}#{seen[app]}", config);
        }
    }

    List<PaneConfig> Enabled() => _config.Current.Panes.Where(p => p.Enabled).ToList();

    PaneConfig ConfigOf(Pane pane) => Enabled()[_panes.IndexOf(pane)];

    // Makes the live panes match the config: starts apps that are new to any layout, closes
    // apps no layout uses any more, and shows the active layout's panes.
    void Sync()
    {
        var wanted = new Dictionary<string, PaneConfig>();
        foreach (var layout in _config.Layouts.OrderBy(l => l == _config.Current ? 0 : 1))
        {
            foreach (var (key, config) in Keys(layout))
                wanted.TryAdd(key, config);
        }

        foreach (var (key, pane) in _pool.Where(e => !wanted.ContainsKey(e.Key)).ToList())
        {
            _pool.Remove(key);
            if (_poppedOut.Remove(pane, out var window)) window.CloseQuietly();
            Detach(pane);
            _feed.Clear(pane);
            pane.Close();
        }

        foreach (var (key, config) in wanted.Where(e => !_pool.ContainsKey(e.Key)))
        {
            var pane = CreatePane(key, config);
            _pool[key] = pane;
            _ = StartPaneAsync(pane);
        }

        _panes.Clear();
        foreach (var (key, config) in Keys(_config.Current))
        {
            var pane = _pool[key];
            pane.Bind(config);
            _panes.Add(pane);
        }

        if (_expanded != null && !_panes.Contains(_expanded)) _expanded = null;
        Arrange();
        UpdateTaskbar();
    }

    Pane CreatePane(string key, PaneConfig config)
    {
        var pane = new Pane(key, config, this);
        pane.Alerted += OnAlerted;
        pane.ExpandRequested += ToggleExpand;
        pane.StateChanged += UpdateTaskbar;
        pane.SwitchRequested += (p, app) => ReplacePane(p, app, keepOld: false);
        pane.AddRequested += (p, app) => ReplacePane(p, app, keepOld: true);
        pane.MoveRequested += MovePane;
        pane.RemoveRequested += RemovePane;
        pane.Notified += (p, notice) => _feed.Add(new FeedItem
        {
            Time = DateTime.Now,
            Pane = p,
            App = p.Title,
            Color = p.Accent,
            Sender = notice.FromCount ? "" : notice.Title,
            Message = notice.FromCount ? "New unread message" : notice.Body,
            Serial = notice.Serial,
            NoticeId = notice.Id,
            Tag = notice.Tag,
        });
        pane.NoticeClosed += (p, id) => _feed.Remove(p, id);
        pane.AllRead += p => _feed.Clear(p);
        return pane;
    }

    // Puts a new app in place of the pane, or after it when keepOld is set.
    void ReplacePane(Pane pane, PaneConfig? app, bool keepOld)
    {
        if (!_panes.Contains(pane)) return;
        app ??= CustomAppDialog.Ask(this);
        if (app == null) return;

        var panes = _config.Current.Panes;
        var index = panes.IndexOf(ConfigOf(pane));
        if (keepOld)
        {
            // After the pane's whole split row, so the row stays together.
            var end = index + 1;
            while (end < panes.Count && panes[end].SplitWithPrevious) end++;
            panes.Insert(end, app);
        }
        else
        {
            app.Size = panes[index].Size;
            app.Share = panes[index].Share;
            app.SplitWithPrevious = panes[index].SplitWithPrevious;
            app.Profile = panes[index].Profile;
            panes[index] = app;
        }
        Sync();
        SaveConfig();
    }

    // Puts a new app beside the pane, sharing its row (or column, when side by side).
    public void SplitPane(Pane pane, PaneConfig? app)
    {
        if (!_panes.Contains(pane)) return;
        app ??= CustomAppDialog.Ask(this);
        if (app == null) return;

        var panes = _config.Current.Panes;
        app.SplitWithPrevious = true;
        panes.Insert(panes.IndexOf(ConfigOf(pane)) + 1, app);
        Sync();
        SaveConfig();
    }

    public IEnumerable<string> Profiles() => _config.Layouts
        .SelectMany(l => l.Panes)
        .Select(p => p.Profile)
        .OfType<string>()
        .Where(p => p.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    // Moves the pane's app to another sign-in profile, in every layout that shows it. The pane
    // reloads under the new profile.
    public void SetProfile(Pane pane, string? profile)
    {
        if (!_panes.Contains(pane)) return;
        if (_poppedOut.ContainsKey(pane))
        {
            MessageBox.Show(this, "Put the pane back in the hub before changing its sign-in profile.", "Message Hub");
            return;
        }

        if (profile != null)
        {
            // WebView2 profile names allow letters, digits and a few symbols, and ignore case.
            profile = new string(profile.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
            if (profile.Length > 50) profile = profile[..50];
            if (profile.Length == 0 || profile.Equals("Default", StringComparison.OrdinalIgnoreCase)) profile = null;
            else profile = Profiles().FirstOrDefault(p => p.Equals(profile, StringComparison.OrdinalIgnoreCase)) ?? profile;
        }

        foreach (var layout in _config.Layouts)
        {
            foreach (var (key, config) in Keys(layout).ToList())
            {
                if (key == pane.Key) config.Profile = profile;
            }
        }
        Sync();
        SaveConfig();
    }

    public void Unsplit(Pane pane)
    {
        pane.Config.SplitWithPrevious = false;
        Arrange();
        SaveConfig();
    }

    void MovePane(Pane pane, int step)
    {
        var from = _panes.IndexOf(pane);
        var to = from + step;
        if (from < 0 || to < 0 || to >= _panes.Count) return;

        var panes = _config.Current.Panes;
        var enabled = Enabled();
        var a = panes.IndexOf(enabled[from]);
        var b = panes.IndexOf(enabled[to]);
        (panes[a], panes[b]) = (panes[b], panes[a]);

        // The split flags belong to the slots, so the rows keep their shape and only the apps move.
        (panes[a].SplitWithPrevious, panes[b].SplitWithPrevious) = (panes[b].SplitWithPrevious, panes[a].SplitWithPrevious);
        Sync();
        SaveConfig();
    }

    void RemovePane(Pane pane)
    {
        if (_panes.Count == 1 || !_panes.Contains(pane)) return;
        var panes = _config.Current.Panes;
        var config = ConfigOf(pane);
        var next = panes.IndexOf(config) + 1;

        // Removing the first pane of a split row leaves the next one heading that row.
        if (!config.SplitWithPrevious && next < panes.Count) panes[next].SplitWithPrevious = false;
        panes.Remove(config);
        Sync();
        SaveConfig();
    }

    public void SaveConfig()
    {
        try
        {
            _config.Save();
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Couldn't save config.json.\n\n{ex.Message}", "Message Hub");
        }
    }

    void SaveState()
    {
        try
        {
            _state.Save();
        }
        catch (IOException)
        {
        }
    }

    void ToggleExpand(Pane pane)
    {
        if (!_panes.Contains(pane) || _poppedOut.ContainsKey(pane)) return;
        _expanded = _expanded == pane ? null : pane;
        Arrange();
    }

    // Stacked and side-by-side layouts get a draggable divider between each pair of panes. A pane
    // split with the one before it shares that pane's row (or column), with its own divider.
    // With a pane expanded, the layout is always stacked and the others keep just their headers.
    // Panes from other layouts sit in a zero-height row so they keep running unseen.
    void Arrange()
    {
        var shown = _panes.Where(p => !_poppedOut.ContainsKey(p)).ToList();
        var parked = _pool.Values.Except(_panes).Where(p => !_poppedOut.ContainsKey(p)).ToList();

        foreach (var child in _grid.Children.OfType<UIElement>()
                     .Where(c => c is GridSplitter || (c is Pane p && !shown.Contains(p) && !parked.Contains(p))).ToList())
            _grid.Children.Remove(child);

        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        foreach (var pane in _panes) pane.SetExpanded(pane == _expanded);

        var used = new HashSet<Grid>();
        var arrangement = _expanded != null ? "stacked" : _config.Current.Arrangement.ToLowerInvariant();
        if (arrangement == "grid") ArrangeGrid(shown);
        else ArrangeLine(shown, columns: arrangement == "side-by-side", used);

        foreach (var (key, split) in _splitGrids.Where(e => !used.Contains(e.Value)).ToList())
        {
            _splitGrids.Remove(key);
            split.Children.Clear();
            _grid.Children.Remove(split);
        }

        if (parked.Count == 0) return;
        if (_grid.RowDefinitions.Count == 0) AddTrack(_grid, false, new GridLength(1, GridUnitType.Star));
        var row = _grid.RowDefinitions.Count;
        AddTrack(_grid, false, new GridLength(0));
        foreach (var pane in parked)
        {
            Place(_grid, pane, row, 0);
            Grid.SetColumnSpan(pane, Math.Max(1, _grid.ColumnDefinitions.Count));
        }
    }

    // Groups panes that share a row: each pane split with the one before joins its group.
    List<List<Pane>> Groups(List<Pane> shown)
    {
        var groups = new List<List<Pane>>();
        foreach (var pane in shown)
        {
            if (pane.Config.SplitWithPrevious && groups.Count > 0 && _expanded == null) groups[^1].Add(pane);
            else groups.Add(new List<Pane> { pane });
        }
        return groups;
    }

    void ArrangeLine(List<Pane> shown, bool columns, HashSet<Grid> used)
    {
        var groups = Groups(shown);
        var cells = new List<UIElement>();
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var collapsed = _expanded != null && group[0] != _expanded;
            var size = _expanded == null ? Math.Max(group[0].Config.Size, 0.1) : 1;
            AddTrack(_grid, columns, collapsed ? new GridLength(Pane.HeaderHeight + 4) : new GridLength(size, GridUnitType.Star));

            UIElement cell = group.Count == 1 ? group[0] : SplitGrid(group, across: !columns, used);
            cells.Add(cell);
            Place(_grid, cell, columns ? 0 : i * 2, columns ? i * 2 : 0);

            if (i == groups.Count - 1) break;

            AddTrack(_grid, columns, new GridLength(_expanded == null ? 5 : 1));
            if (_expanded != null) continue;

            var divider = Divider(columns);
            divider.DragCompleted += (_, _) => SaveSizes(groups, cells, columns);
            Place(_grid, divider, columns ? 0 : i * 2 + 1, columns ? i * 2 + 1 : 0);
        }
    }

    // The panes of one split row (or column) side by side in their own grid. The grid is kept
    // between arranges so its panes aren't moved, which would reload them.
    Grid SplitGrid(List<Pane> group, bool across, HashSet<Grid> used)
    {
        var key = string.Join("|", group.Select(p => p.Key)) + (across ? "|across" : "|down");
        if (!_splitGrids.TryGetValue(key, out var split))
        {
            split = new Grid();
            _splitGrids[key] = split;
        }
        used.Add(split);

        foreach (var divider in split.Children.OfType<GridSplitter>().ToList()) split.Children.Remove(divider);
        split.RowDefinitions.Clear();
        split.ColumnDefinitions.Clear();

        for (var j = 0; j < group.Count; j++)
        {
            AddTrack(split, across, new GridLength(Math.Max(group[j].Config.Share, 0.1), GridUnitType.Star));
            Place(split, group[j], across ? 0 : j * 2, across ? j * 2 : 0);
            if (j == group.Count - 1) break;

            AddTrack(split, across, new GridLength(5));
            var divider = Divider(across);
            divider.DragCompleted += (_, _) => SaveShares(group, split, across);
            Place(split, divider, across ? 0 : j * 2 + 1, across ? j * 2 + 1 : 0);
        }
        return split;
    }

    static GridSplitter Divider(bool columns) => new()
    {
        ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)),
        ShowsPreview = false,
        ToolTip = "Drag to resize",
    };

    // As square as possible: 4 apps make 2×2, 6 make 3×2. A short last row is stretched to fill.
    void ArrangeGrid(List<Pane> shown)
    {
        if (shown.Count == 0) return;
        var columns = (int)Math.Ceiling(Math.Sqrt(shown.Count));
        var rows = (int)Math.Ceiling(shown.Count / (double)columns);
        for (var c = 0; c < columns; c++) AddTrack(_grid, true, new GridLength(1, GridUnitType.Star));
        for (var r = 0; r < rows; r++) AddTrack(_grid, false, new GridLength(1, GridUnitType.Star));

        for (var i = 0; i < shown.Count; i++)
        {
            Place(_grid, shown[i], i / columns, i % columns);
            if (i == shown.Count - 1) Grid.SetColumnSpan(shown[i], columns - i % columns);
        }
    }

    static void AddTrack(Grid grid, bool column, GridLength length)
    {
        if (column) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        else grid.RowDefinitions.Add(new RowDefinition { Height = length });
    }

    // Moves the element into the grid if it's somewhere else, then sets its cell.
    static void Place(Grid grid, UIElement element, int row, int column)
    {
        if (element is FrameworkElement { Parent: Panel other } && other != grid) other.Children.Remove(element);
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, 1);
        Grid.SetColumnSpan(element, 1);
        if (!grid.Children.Contains(element)) grid.Children.Add(element);
    }

    static void Detach(UIElement element)
    {
        if (element is FrameworkElement { Parent: Panel panel }) panel.Children.Remove(element);
    }

    void SaveSizes(List<List<Pane>> groups, List<UIElement> cells, bool columns)
    {
        double Length(UIElement cell) => columns
            ? _grid.ColumnDefinitions[Grid.GetColumn(cell)].ActualWidth
            : _grid.RowDefinitions[Grid.GetRow(cell)].ActualHeight;

        var total = cells.Sum(Length);
        if (total <= 0) return;

        for (var i = 0; i < groups.Count; i++)
            groups[i][0].Config.Size = Math.Round(Length(cells[i]) / total * groups.Count, 3);
        SaveConfig();
    }

    void SaveShares(List<Pane> group, Grid split, bool across)
    {
        double Length(Pane p) => across
            ? split.ColumnDefinitions[Grid.GetColumn(p)].ActualWidth
            : split.RowDefinitions[Grid.GetRow(p)].ActualHeight;

        var total = group.Sum(Length);
        if (total <= 0) return;

        foreach (var pane in group)
            pane.Config.Share = Math.Round(Length(pane) / total * group.Count, 3);
        SaveConfig();
    }

    void OnAlerted(Pane pane, bool vip)
    {
        var hidden = !IsVisible || WindowState == WindowState.Minimized;

        if (_config.FlashTaskbar && IsVisible && !IsActive) Native.FlashTaskbar(this);

        if (_config.Sound && DateTime.Now - _lastSound > TimeSpan.FromSeconds(3))
        {
            _lastSound = DateTime.Now;
            Sounds.Play(pane.Config.Sound);
        }

        if (hidden)
        {
            _notifiedPane = pane;
            _tray.Notify(pane.Title, pane.LatestText);
        }

        if (vip && _config.ExpandOnVip && _panes.Contains(pane) && _expanded != pane) ToggleExpand(pane);
    }

    // Ctrl+1..9 expands that pane, Ctrl+0 puts them all back, Ctrl+Shift+F opens or closes the feed.
    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F)
        {
            ShowFeed();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        if (e.Key is >= Key.D1 and <= Key.D9 && e.Key - Key.D1 < _panes.Count)
        {
            ToggleExpand(_panes[e.Key - Key.D1]);
            e.Handled = true;
        }
        else if (e.Key == Key.D0 && _expanded != null)
        {
            ToggleExpand(_expanded);
            e.Handled = true;
        }
    }

    void UpdateTaskbar()
    {
        var alerting = _pool.Values.Where(p => p.Alerting).ToList();
        var unread = _pool.Values.Sum(p => Math.Max(p.Unread, 0));

        Title = alerting.Count > 0 ? $"Message Hub · new in {string.Join(", ", alerting.Select(p => p.Title))}"
            : Snoozed ? $"Message Hub · do not disturb {SnoozeText}"
            : unread > 0 ? $"Message Hub ({unread})"
            : "Message Hub";
        TaskbarItemInfo.Overlay = alerting.Count > 0 ? Badge(alerting.Count > 9 ? "9+" : alerting.Count.ToString(), AlertRed)
            : Snoozed ? Badge("z", Color.FromRgb(0x6E, 0x6E, 0x73))
            : null;
        TaskbarItemInfo.Description = Title;

        // Alerts from apps in other layouts, which have no header on screen.
        var elsewhere = alerting.Where(p => !_panes.Contains(p)).ToList();
        _alertBar.Visibility = elsewhere.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _alertText.Text = elsewhere.Count == 0 ? ""
            : $"New in {string.Join(", ", elsewhere.Select(p => p.Title))} · click to open";
    }

    ImageSource Badge(string label, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(color), null, new Point(16, 16), 16, 16);
            var text = new FormattedText(
                label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 20, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(16 - text.Width / 2, 16 - text.Height / 2));
        }

        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    void ApplySavedBounds(HubState state)
    {
        Width = state.Width ?? 1000;
        Height = state.Height ?? Math.Min(1400, SystemParameters.WorkArea.Height);

        if (state.Left is { } left && state.Top is { } top
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (state.Maximized) WindowState = WindowState.Maximized;
    }

    // Closing minimises to the taskbar unless that's turned off or Message Hub is quitting. It stays
    // on the taskbar (not just the tray) so clicking its button restores this window: clicking a
    // pinned icon with no window open would start a second copy, which some security policies
    // stop with a "content is blocked" prompt.
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (IsVisible)
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _state.Left = bounds.Left;
            _state.Top = bounds.Top;
            _state.Width = bounds.Width;
            _state.Height = bounds.Height;
            _state.Maximized = (WindowState == WindowState.Minimized ? _lastShownState : WindowState) == WindowState.Maximized;
        }

        if (!Quitting && _config.CloseToTray)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            if (!_state.TrayHintShown)
            {
                _state.TrayHintShown = true;
                _tray.Notify("Message Hub is still running", "It's minimised and keeps alerting. To quit, right-click the tray icon.");
            }
            SaveState();
            return;
        }

        Quitting = true;
        SaveState();
        foreach (var window in _poppedOut.Values.ToList()) window.CloseQuietly();
        _feedWindow?.CloseQuietly();
        _tray.Dispose();
    }
}
