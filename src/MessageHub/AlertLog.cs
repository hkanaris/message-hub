using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MessageHub;

// Every notification an app sent this session, newest first, and what Message Hub did with it.
public static class AlertLog
{
    const int Limit = 200;

    public sealed record Entry(DateTime Time, string App, string Message, string Outcome);

    public static ObservableCollection<Entry> Entries { get; } = new();

    public static void Add(string app, string message, string outcome)
    {
        Entries.Insert(0, new Entry(DateTime.Now, app, message, outcome));
        while (Entries.Count > Limit) Entries.RemoveAt(Entries.Count - 1);
    }
}

public sealed class AlertLogWindow : Window
{
    public AlertLogWindow()
    {
        Title = "Recent alerts · Message Hub";
        Width = 760;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new DataGrid
        {
            ItemsSource = AlertLog.Entries,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new Binding(nameof(AlertLog.Entry.Time)) { StringFormat = "HH:mm:ss" } });
        grid.Columns.Add(new DataGridTextColumn { Header = "App", Binding = new Binding(nameof(AlertLog.Entry.App)) });
        grid.Columns.Add(new DataGridTextColumn { Header = "What happened", Binding = new Binding(nameof(AlertLog.Entry.Outcome)) });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Message",
            Binding = new Binding(nameof(AlertLog.Entry.Message)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });

        var empty = new TextBlock
        {
            Text = "Nothing yet. Use Alerts → Send test alert on a pane, or wait for a message.",
            Margin = new Thickness(16),
        };

        var layout = new DockPanel();
        DockPanel.SetDock(empty, Dock.Top);
        layout.Children.Add(empty);
        layout.Children.Add(grid);
        Content = layout;

        void UpdateEmpty() => empty.Visibility = AlertLog.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmpty();
        void OnChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateEmpty();
        AlertLog.Entries.CollectionChanged += OnChanged;
        Closed += (_, _) => AlertLog.Entries.CollectionChanged -= OnChanged;
    }
}
