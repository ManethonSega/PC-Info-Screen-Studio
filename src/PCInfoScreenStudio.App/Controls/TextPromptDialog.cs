using System.Windows;
using System.Windows.Controls;

namespace PCInfoScreenStudio.Controls;

internal sealed class TextPromptDialog : Window
{
    private readonly TextBox _textBox;

    private TextPromptDialog(Window? owner, string title, string prompt, string initialValue)
    {
        Owner = owner;
        Title = title;
        Width = 390;
        Height = 180;
        MinWidth = 320;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;

        _textBox = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 6, 0, 12)
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 78 };
        ok.Click += (_, _) => { DialogResult = true; Close(); };

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 78 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public static string? Show(Window? owner, string title, string prompt, string initialValue)
    {
        var dialog = new TextPromptDialog(owner, title, prompt, initialValue);
        return dialog.ShowDialog() == true ? dialog._textBox.Text.Trim() : null;
    }
}
