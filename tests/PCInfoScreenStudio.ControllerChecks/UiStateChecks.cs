using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class UiStateChecks
{
    public static FrameworkElement Gallery()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Studio controls", FontSize = 20, Margin = new Thickness(0, 0, 0, 14) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Button { Content = "Save theme", Style = (Style)Application.Current.Resources["PrimaryButtonStyle"] });
        row.Children.Add(new Button { Content = "Disabled action", IsEnabled = false });
        panel.Children.Add(row);
        panel.Children.Add(new CheckBox { Content = "Start with Windows", IsChecked = true });
        panel.Children.Add(new CheckBox { Content = "Live sensors are always enabled", IsChecked = true, IsEnabled = false });
        panel.Children.Add(new ComboBoxItem { Content = "Selected source: CPU temperature", IsSelected = true, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(new ListBoxItem { Content = "Selected layer: Clock overlay", IsSelected = true });
        panel.Children.Add(new TextBox { Text = "Type a sensor name or change a value", Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(new Slider { Value = 60, Minimum = 0, Maximum = 100 });
        var tip = CreateToolTip();
        tip.ApplyTemplate();
        tip.Measure(new Size(520, double.PositiveInfinity));
        tip.Arrange(new Rect(tip.DesiredSize));
        tip.UpdateLayout();
        var tipBitmap = new RenderTargetBitmap((int)Math.Ceiling(tip.ActualWidth), (int)Math.Ceiling(tip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        tipBitmap.Render(tip);
        panel.Children.Add(new Image { Source = tipBitmap, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new Expander { Header = "CAPTION SETTINGS", IsExpanded = true, Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = "Each section keeps its existing controls and expanded state.", TextWrapping = TextWrapping.Wrap } });
        return new Border { Background = (Brush)Application.Current.Resources["PanelBrush"], Padding = new Thickness(18), Child = panel };
    }
    public static void Check()
    {
        var gallery = Gallery();
        var host = new Window { Content = gallery, Width = 660, Height = 460, ShowInTaskbar = false };
        try
        {
            host.Show();
            host.UpdateLayout();
            var primary = (Button)Find(gallery, typeof(Button))!;
            primary.ApplyTemplate();
            var primaryBorder = (Border)primary.Template.FindName("ButtonBorder", primary);
            Assert(Contrast((SolidColorBrush)primary.Foreground, (SolidColorBrush)primaryBorder.Background) >= 4.5,
                "The primary action needs dark, readable text on its actual cyan background.");
            Assert(Contrast((SolidColorBrush)Application.Current.Resources["MutedTextBrush"],
                (SolidColorBrush)Application.Current.Resources["PanelBrush"]) >= 4.5,
                "Helper labels must remain readable on the panel background.");
            var slider = (Slider)Find(gallery, typeof(Slider))!;
            slider.ApplyTemplate();
            var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
            slider.Value = 75;
            host.UpdateLayout();
            Assert(Math.Abs(track.Value - 75) < .001 && track.Thumb.ActualWidth > 0,
                "The styled slider must track the bound value and retain its drag handle.");
            foreach (var type in new[] { typeof(ComboBoxItem), typeof(ListBoxItem) })
            {
                var item = (Control)Find(gallery, type)!;
                item.ApplyTemplate();
                var border = (Border)item.Template.FindName("ItemBorder", item);
                Assert(Contrast((SolidColorBrush)item.Foreground, (SolidColorBrush)border.Background) >= 4.5,
                    "Selected items must have readable text on their actual rendered backgrounds.");
            }
            var tip = CreateToolTip();
            tip.PlacementTarget = gallery;
            try
            {
                tip.IsOpen = true;
                tip.ApplyTemplate();
                tip.UpdateLayout();
                var tipText = (TextBlock)Find(tip, typeof(TextBlock))!;
                var tipBorder = (Border)Find(tip, typeof(Border))!;
                Assert(Contrast((SolidColorBrush)tipText.Foreground, (SolidColorBrush)tipBorder.Background) >= 4.5,
                    "The actual popup tooltip text and background must remain readable.");
            }
            finally { tip.IsOpen = false; }
            Console.WriteLine("PASS: primary action, helper labels, selected items, tooltip contrast and styled slider.");
        }
        finally { host.Close(); }
    }
    private static ToolTip CreateToolTip() => new()
    {
        Style = (Style)Application.Current.Resources[typeof(ToolTip)],
        Content = "Remove this photo from the playlist. The original file stays on your PC.", MaxWidth = 520
    };
    private static DependencyObject? Find(DependencyObject element, Type type)
    {
        if (element.GetType() == type) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (Find(VisualTreeHelper.GetChild(element, i), type) is { } found) return found;
        return null;
    }
    private static double Contrast(SolidColorBrush text, SolidColorBrush background)
    {
        double L(Color c)
        {
            double Linear(byte value) { var n = value / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var a = L(text.Color); var b = L(background.Color);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
