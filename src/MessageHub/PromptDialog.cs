using System.Windows;
using System.Windows.Controls;

namespace MessageHub;

// Asks for one line of text.
public sealed class PromptDialog : Window
{
    readonly TextBox _text = new() { Margin = new Thickness(0, 4, 0, 16) };
    string? _result;

    PromptDialog(string title, string label, string value)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _text.Text = value;

        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        ok.Click += (_, _) =>
        {
            _result = _text.Text.Trim();
            DialogResult = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = label });
        form.Children.Add(_text);
        form.Children.Add(buttons);
        Content = form;

        Loaded += (_, _) =>
        {
            _text.Focus();
            _text.SelectAll();
        };
    }

    public static string? Ask(Window owner, string title, string label, string value = "")
    {
        var dialog = new PromptDialog(title, label, value) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
