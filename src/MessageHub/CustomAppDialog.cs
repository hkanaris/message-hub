using System.Windows;
using System.Windows.Controls;

namespace MessageHub;

// Asks for a name and web address for an app that isn't in the catalogue.
public sealed class CustomAppDialog : Window
{
    readonly TextBox _name = new() { Margin = new Thickness(0, 4, 0, 12) };
    readonly TextBox _url = new() { Margin = new Thickness(0, 4, 0, 16), Text = "https://" };
    PaneConfig? _result;

    CustomAppDialog()
    {
        Title = "Custom app";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = "Add", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        ok.Click += (_, _) => Accept();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = "Name" });
        form.Children.Add(_name);
        form.Children.Add(new TextBlock { Text = "Web address" });
        form.Children.Add(_url);
        form.Children.Add(buttons);
        Content = form;

        Loaded += (_, _) => _name.Focus();
    }

    public static PaneConfig? Ask(Window owner)
    {
        var dialog = new CustomAppDialog { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }

    void Accept()
    {
        var url = _url.Text.Trim();
        if (!url.Contains("://")) url = "https://" + url;

        if (string.IsNullOrWhiteSpace(_name.Text)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            MessageBox.Show(this, "Enter a name and a web address, like https://app.example.com.", "Custom app");
            return;
        }

        _result = new PaneConfig { Name = _name.Text.Trim(), Url = uri.AbsoluteUri, Color = "#9AA0A6" };
        DialogResult = true;
    }
}
