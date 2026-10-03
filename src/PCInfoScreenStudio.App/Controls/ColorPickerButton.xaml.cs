using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace PCInfoScreenStudio.Controls;

public partial class ColorPickerButton : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty HexColorProperty = DependencyProperty.Register(
        nameof(HexColor), typeof(string), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata("#FFFFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    public ColorPickerButton()
    {
        InitializeComponent();
        UpdateSwatch(HexColor);
    }

    public string HexColor
    {
        get => (string)GetValue(HexColorProperty);
        set => SetValue(HexColorProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColorPickerButton)d).UpdateSwatch(e.NewValue?.ToString());

    private void UpdateSwatch(string? value)
    {
        try
        {
            Swatch.Background = new SolidColorBrush(
                (MediaColor)ColorConverter.ConvertFromString(value ?? "#FFFFFFFF"));
        }
        catch
        {
            Swatch.Background = Brushes.Magenta;
        }
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        var current = Parse(HexColor);
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B)
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        var c = dialog.Color;
        HexColor = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static MediaColor Parse(string? value)
    {
        try
        {
            return (MediaColor)ColorConverter.ConvertFromString(value ?? "#FFFFFFFF");
        }
        catch
        {
            return Colors.White;
        }
    }
}
