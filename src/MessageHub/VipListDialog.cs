using System.Windows;
using System.Windows.Controls;

namespace MessageHub;

// Edits the VIP list: one name, chat or channel per line.
public sealed class VipListDialog : Window
{
    readonly TextBox _list = new()
    {
        AcceptsReturn = true,
        Height = 180,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 6, 0, 16),
    };
    List<string>? _result;

    VipListDialog(IEnumerable<string> current)
    {
        Title = "VIP list";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _list.Text = string.Join(Environment.NewLine, current);

        var save = new Button { Content = "Save", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        save.Click += (_, _) =>
        {
            _result = _list.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
            DialogResult = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock
        {
            Text = "One per line: people, chats or channels. A message mentioning any of them is a VIP message.",
            TextWrapping = TextWrapping.Wrap,
        });
        form.Children.Add(_list);
        form.Children.Add(buttons);
        Content = form;

        Loaded += (_, _) => _list.Focus();
    }

    public static List<string>? Ask(Window owner, IEnumerable<string> current)
    {
        var dialog = new VipListDialog(current) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
