using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;

namespace MessageHub;

// The panes stacked top to bottom. One pane can be expanded; the rest shrink to their headers.
public sealed class MainWindow : Window
{
    readonly HubConfig _config;
    readonly HubState _state;
    readonly Grid _grid = new();
    readonly List<Pane> _panes = new();
    readonly List<PaneConfig> _hidden;
    readonly TaskCompletionSource<CoreWebView2Environment> _environment = new();
    readonly string _script = ReadScript();
    Pane? _expanded;
    DateTime _lastSound = DateTime.MinValue;

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
        var pane = new Pane(config, _config, _state);
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

    void SaveConfig()
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

    // Lays the panes out top to bottom with a draggable divider between each pair.
    // With one pane expanded, the others keep just their headers and the dividers are hidden.
    void Arrange()
    {
        foreach (var child in _grid.Children.OfType<UIElement>()
                     .Where(c => c is GridSplitter || (c is Pane p && !_panes.Contains(p))).ToList())
            _grid.Children.Remove(child);

        _grid.RowDefinitions.Clear();
        for (var i = 0; i < _panes.Count; i++)
        {
            var pane = _panes[i];
            var collapsed = _expanded != null && pane != _expanded;
            var size = _expanded == null ? Math.Max(pane.Config.Size, 0.1) : 1;
            _grid.RowDefinitions.Add(new RowDefinition
            {
                Height = collapsed ? new GridLength(Pane.HeaderHeight + 4) : new GridLength(size, GridUnitType.Star),
            });
            Grid.SetRow(pane, i * 2);
            if (!_grid.Children.Contains(pane)) _grid.Children.Add(pane);
            pane.SetExpanded(pane == _expanded);

            if (i == _panes.Count - 1) break;

            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_expanded == null ? 5 : 1) });
            if (_expanded != null) continue;

            var divider = new GridSplitter
            {
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)),
                ShowsPreview = false,
                ToolTip = "Drag to resize",
            };
            divider.DragCompleted += (_, _) => SaveSizes();
            Grid.SetRow(divider, i * 2 + 1);
            _grid.Children.Add(divider);
        }
    }

    void SaveSizes()
    {
        var total = _panes.Sum(p => _grid.RowDefinitions[Grid.GetRow(p)].ActualHeight);
        if (total <= 0) return;

        foreach (var pane in _panes)
            pane.Config.Size = Math.Round(_grid.RowDefinitions[Grid.GetRow(pane)].ActualHeight / total * _panes.Count, 3);
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
            : unread > 0 ? $"Message Hub ({unread})"
            : "Message Hub";
        TaskbarItemInfo.Overlay = alerting.Count > 0 ? Badge(alerting.Count) : null;
        TaskbarItemInfo.Description = Title;
    }

    ImageSource Badge(int count)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE0, 0x1E, 0x2E)), null, new Point(16, 16), 16, 16);
            var text = new FormattedText(
                count > 9 ? "9+" : count.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
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
        try
        {
            _state.Save();
        }
        catch (IOException)
        {
        }
    }
}
