using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace MessageHub;

// The app panes, laid out stacked, side by side or in a grid. One pane can be expanded;
// the rest shrink to their headers.
public sealed class MainWindow : Window
{
    readonly HubConfig _config;
    readonly HubState _state;
    readonly Grid _grid = new();
    readonly List<Pane> _panes = new();
    readonly List<PaneConfig> _hidden;
    readonly TaskCompletionSource<CoreWebView2Environment> _environment = new();
    readonly string _script = ReadScript();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    Pane? _expanded;
    DateTime _lastSound = DateTime.MinValue;
    DateTime _nextUpdateCheck = DateTime.Now.AddSeconds(20);
    Version? _declinedUpdate;
    bool _updating;

    public HubConfig Config => _config;
    public HubState State => _state;
    public int PaneCount => _panes.Count;

    public bool Snoozed => _state.SnoozedUntil is { } until && until > DateTime.Now;

    public string SnoozeText => _state.SnoozedUntil == DateTime.MaxValue
        ? "until turned off"
        : $"until {_state.SnoozedUntil:HH:mm}";

    public MainWindow(HubConfig config, HubState state)
    {
        _config = config;
        _state = state;

        Title = "Message Hub";
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1C));
        TaskbarItemInfo = new TaskbarItemInfo();
        Content = _grid;
        ApplySavedBounds(state);

        _hidden = config.Panes.Where(p => !p.Enabled).ToList();
        foreach (var paneConfig in config.Panes.Where(p => p.Enabled))
            _panes.Add(CreatePane(paneConfig));
        Arrange();

        Loaded += async (_, _) => await LoadPanesAsync();
        Closing += OnClosing;
        PreviewKeyDown += OnKey;
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        UpdateTaskbar();
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
            foreach (var pane in _panes) pane.ClearAlert();
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

    public void SetLayout(string layout)
    {
        _config.Layout = layout;
        Arrange();
        SaveConfig();
    }

    // Adds apps not already showing, or removes panes from the end, to reach the count.
    public void SetPaneCount(int count)
    {
        while (_panes.Count > count)
        {
            var last = _panes[^1];
            _panes.Remove(last);
            if (_expanded == last) _expanded = null;
            last.Close();
        }

        var added = new List<Pane>();
        while (_panes.Count < count)
        {
            var app = AppCatalog.Apps.FirstOrDefault(a => _panes.All(p => p.Config.Url != a.Url)) ?? AppCatalog.Apps[0];
            var pane = CreatePane(app.Copy());
            _panes.Add(pane);
            added.Add(pane);
        }

        Arrange();
        SaveConfig();
        UpdateTaskbar();
        foreach (var pane in added) _ = StartPaneAsync(pane);
    }

    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updating) return;
        try
        {
            var latest = await Updater.GetLatestAsync();
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
            Restart();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            _updating = false;
            UpdateTaskbar();
            if (manual) MessageBox.Show(this, $"Couldn't check for updates.\n\n{ex.Message}", "Message Hub");
        }
    }

    public static void OpenSettings() =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{HubConfig.FilePath}\"") { UseShellExecute = true });

    public static void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
        Application.Current.Shutdown();
    }

    async Task LoadPanesAsync()
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
            Close();
            return;
        }

        foreach (var pane in _panes.ToList())
            await StartPaneAsync(pane);
    }

    async Task StartPaneAsync(Pane pane)
    {
        var environment = await _environment.Task;
        if (!_panes.Contains(pane)) return;
        try
        {
            await pane.InitAsync(environment, _script);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{pane.Title} couldn't start.\n\n{ex.Message}", "Message Hub");
        }
    }

    Pane CreatePane(PaneConfig config)
    {
        var pane = new Pane(config, this);
        pane.Alerted += OnAlerted;
        pane.ExpandRequested += ToggleExpand;
        pane.StateChanged += UpdateTaskbar;
        pane.SwitchRequested += (p, app) => ReplacePane(p, app, keepOld: false);
        pane.AddRequested += (p, app) => ReplacePane(p, app, keepOld: true);
        pane.MoveRequested += MovePane;
        pane.RemoveRequested += RemovePane;
        return pane;
    }

    // Puts a new app in place of the pane, or below it when keepOld is set.
    void ReplacePane(Pane pane, PaneConfig? app, bool keepOld)
    {
        app ??= CustomAppDialog.Ask(this);
        if (app == null) return;

        var index = _panes.IndexOf(pane);
        if (!keepOld) app.Size = pane.Config.Size;
        var added = CreatePane(app);
        if (keepOld)
        {
            _panes.Insert(index + 1, added);
        }
        else
        {
            _panes[index] = added;
            if (_expanded == pane) _expanded = added;
            pane.Close();
        }

        Arrange();
        SaveConfig();
        UpdateTaskbar();
        _ = StartPaneAsync(added);
    }

    void MovePane(Pane pane, int step)
    {
        var index = _panes.IndexOf(pane);
        var target = index + step;
        if (target < 0 || target >= _panes.Count) return;

        (_panes[index], _panes[target]) = (_panes[target], _panes[index]);
        Arrange();
        SaveConfig();
    }

    void RemovePane(Pane pane)
    {
        if (_panes.Count == 1) return;

        _panes.Remove(pane);
        if (_expanded == pane) _expanded = null;
        pane.Close();
        Arrange();
        SaveConfig();
        UpdateTaskbar();
    }

    public void SaveConfig()
    {
        _config.Panes = _panes.Select(p => p.Config).Concat(_hidden).ToList();
        try
        {
            _config.Save();
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Couldn't save config.json.\n\n{ex.Message}", "Message Hub");
        }
    }

    static string ReadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HubScript.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    void ToggleExpand(Pane pane)
    {
        _expanded = _expanded == pane ? null : pane;
        Arrange();
    }

    // Stacked and side-by-side layouts get a draggable divider between each pair of panes.
    // With a pane expanded, the layout is always stacked and the others keep just their headers.
    void Arrange()
    {
        foreach (var child in _grid.Children.OfType<UIElement>()
                     .Where(c => c is GridSplitter || (c is Pane p && !_panes.Contains(p))).ToList())
            _grid.Children.Remove(child);

        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        foreach (var pane in _panes) pane.SetExpanded(pane == _expanded);

        var layout = _expanded != null ? "stacked" : _config.Layout.ToLowerInvariant();
        if (layout == "grid") ArrangeGrid();
        else ArrangeLine(columns: layout == "side-by-side");
    }

    void ArrangeLine(bool columns)
    {
        for (var i = 0; i < _panes.Count; i++)
        {
            var pane = _panes[i];
            var collapsed = _expanded != null && pane != _expanded;
            var size = _expanded == null ? Math.Max(pane.Config.Size, 0.1) : 1;
            AddTrack(columns, collapsed ? new GridLength(Pane.HeaderHeight + 4) : new GridLength(size, GridUnitType.Star));
            Place(pane, columns ? 0 : i * 2, columns ? i * 2 : 0);

            if (i == _panes.Count - 1) break;

            AddTrack(columns, new GridLength(_expanded == null ? 5 : 1));
            if (_expanded != null) continue;

            var divider = new GridSplitter
            {
                ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)),
                ShowsPreview = false,
                ToolTip = "Drag to resize",
            };
            divider.DragCompleted += (_, _) => SaveSizes(columns);
            Place(divider, columns ? 0 : i * 2 + 1, columns ? i * 2 + 1 : 0);
        }
    }

    // As square as possible: 4 apps make 2×2, 6 make 3×2. A short last row is stretched to fill.
    void ArrangeGrid()
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(_panes.Count));
        var rows = (int)Math.Ceiling(_panes.Count / (double)columns);
        for (var c = 0; c < columns; c++) AddTrack(true, new GridLength(1, GridUnitType.Star));
        for (var r = 0; r < rows; r++) AddTrack(false, new GridLength(1, GridUnitType.Star));

        for (var i = 0; i < _panes.Count; i++)
        {
            Place(_panes[i], i / columns, i % columns);
            if (i == _panes.Count - 1) Grid.SetColumnSpan(_panes[i], columns - i % columns);
        }
    }

    void AddTrack(bool column, GridLength length)
    {
        if (column) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        else _grid.RowDefinitions.Add(new RowDefinition { Height = length });
    }

    void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, 1);
        Grid.SetColumnSpan(element, 1);
        if (!_grid.Children.Contains(element)) _grid.Children.Add(element);
    }

    void SaveSizes(bool columns)
    {
        double Length(Pane p) => columns
            ? _grid.ColumnDefinitions[Grid.GetColumn(p)].ActualWidth
            : _grid.RowDefinitions[Grid.GetRow(p)].ActualHeight;

        var total = _panes.Sum(Length);
        if (total <= 0) return;

        foreach (var pane in _panes)
            pane.Config.Size = Math.Round(Length(pane) / total * _panes.Count, 3);
        SaveConfig();
    }

    void OnAlerted(Pane pane, bool vip)
    {
        if (_config.FlashTaskbar && !IsActive) Native.FlashTaskbar(this);

        if (_config.Sound && DateTime.Now - _lastSound > TimeSpan.FromSeconds(3))
        {
            _lastSound = DateTime.Now;
            SystemSounds.Asterisk.Play();
        }

        if (vip && _config.ExpandOnVip && _expanded != pane) ToggleExpand(pane);
    }

    // Ctrl+1..9 expands that pane, Ctrl+0 puts them all back, Ctrl+, opens settings.
    void OnKey(object sender, KeyEventArgs e)
    {
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
        else if (e.Key == Key.OemComma)
        {
            OpenSettings();
            e.Handled = true;
        }
    }

    void UpdateTaskbar()
    {
        var alerting = _panes.Where(p => p.Alerting).ToList();
        var unread = _panes.Sum(p => Math.Max(p.Unread, 0));

        Title = alerting.Count > 0 ? $"Message Hub · new in {string.Join(", ", alerting.Select(p => p.Title))}"
            : Snoozed ? $"Message Hub · do not disturb {SnoozeText}"
            : unread > 0 ? $"Message Hub ({unread})"
            : "Message Hub";
        TaskbarItemInfo.Overlay = alerting.Count > 0 ? Badge(alerting.Count > 9 ? "9+" : alerting.Count.ToString(), Color.FromRgb(0xE0, 0x1E, 0x2E))
            : Snoozed ? Badge("z", Color.FromRgb(0x6E, 0x6E, 0x73))
            : null;
        TaskbarItemInfo.Description = Title;
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

    void OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _state.Left = bounds.Left;
        _state.Top = bounds.Top;
        _state.Width = bounds.Width;
        _state.Height = bounds.Height;
        _state.Maximized = WindowState == WindowState.Maximized;
        SaveState();
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
}
