using System.Windows;
using System.Windows.Media;

namespace MessageHub;

// A pane in its own window, e.g. on another monitor. Closing it puts the pane back in the hub.
public sealed class PopOutWindow : Window
{
    bool _quiet;

    public Pane Pane { get; }

    public event Action<PopOutWindow>? Returned;

    public PopOutWindow(Pane pane)
    {
        Pane = pane;
        Title = $"{pane.Title} · Message Hub";
        Width = 900;
        Height = 700;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1C));
        Content = pane;

        Closing += (_, _) =>
        {
            Content = null;
            if (!_quiet && !MainWindow.Quitting) Returned?.Invoke(this);
        };
    }

    // Closes without handing the pane back, for when the app is being removed.
    public void CloseQuietly()
    {
        _quiet = true;
        Close();
    }
}
