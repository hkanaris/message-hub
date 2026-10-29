using System.Windows;
using System.Windows.Controls;

namespace MessageHub;

public static class LayoutChoices
{
    public const int MinApps = 2;
    public const int MaxApps = 6;

    public static readonly (string Label, string Value)[] Arrangements =
    {
        ("Stacked", "stacked"),
        ("Side by side", "side-by-side"),
        ("Grid", "grid"),
    };
}

// Sets up a new layout: a name, how the apps are arranged, how many, and which ones.
public sealed class NewLayoutDialog : Window
{
    readonly HashSet<string> _taken;
    readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 12) };
    readonly ComboBox _arrangement = new() { Margin = new Thickness(0, 4, 0, 12) };
    readonly ComboBox _count = new() { Margin = new Thickness(0, 4, 0, 12) };
    readonly StackPanel _apps = new() { Margin = new Thickness(0, 4, 0, 16) };
    LayoutConfig? _result;

    NewLayoutDialog(IEnumerable<string> taken)
    {
        _taken = taken.ToHashSet();
        Title = "New layout";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var (label, _) in LayoutChoices.Arrangements) _arrangement.Items.Add(label);
        _arrangement.SelectedIndex = 1;
        for (var n = LayoutChoices.MinApps; n <= LayoutChoices.MaxApps; n++) _count.Items.Add(n);
        _count.SelectionChanged += (_, _) => ShowAppPickers();
        _count.SelectedIndex = 0;

        var create = new Button { Content = "Create", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        create.Click += (_, _) => Accept();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);

        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = "Name" });
        form.Children.Add(_name);
        form.Children.Add(new TextBlock { Text = "Arrangement" });
        form.Children.Add(_arrangement);
        form.Children.Add(new TextBlock { Text = "Number of apps" });
        form.Children.Add(_count);
        form.Children.Add(new TextBlock { Text = "Apps, in order" });
        form.Children.Add(_apps);
        form.Children.Add(buttons);
        Content = form;

        Loaded += (_, _) => _name.Focus();
    }

    public static LayoutConfig? Ask(Window owner, IEnumerable<string> existingNames)
    {
        var dialog = new NewLayoutDialog(existingNames) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }

    // One dropdown per app; existing choices are kept when the count changes.
    void ShowAppPickers()
    {
        var count = (int)_count.SelectedItem;
        while (_apps.Children.Count > count) _apps.Children.RemoveAt(_apps.Children.Count - 1);
        while (_apps.Children.Count < count)
        {
            var picker = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var app in AppCatalog.Apps) picker.Items.Add(app.Name);
            picker.SelectedIndex = _apps.Children.Count % AppCatalog.Apps.Count;
            _apps.Children.Add(picker);
        }
    }

    void Accept()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || _taken.Contains(name))
        {
            MessageBox.Show(this, name.Length == 0 ? "Give the layout a name." : $"There's already a layout called {name}.", "New layout");
            return;
        }

        _result = new LayoutConfig
        {
            Name = name,
            Arrangement = LayoutChoices.Arrangements[_arrangement.SelectedIndex].Value,
            Panes = _apps.Children.OfType<ComboBox>().Select(c => AppCatalog.Get((string)c.SelectedItem)).ToList(),
        };
        DialogResult = true;
    }
}
