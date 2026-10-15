using System;
using System.Globalization;
using System.Windows.Data;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Controls;

// Presentation labels stay out of the serialized theme model and enum values.
public sealed class WidgetTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        WidgetType.Text => "Text",
        WidgetType.Value => "Sensor value",
        WidgetType.CircularGauge => "Circular gauge",
        WidgetType.AnalogClock => "Analogue clock",
        WidgetType.BarGauge => "Bar gauge",
        WidgetType.Graph => "History graph",
        WidgetType.Image => "Image",
        WidgetType.AnimatedImage => "Animated GIF",
        WidgetType.Video => "Video",
        WidgetType.Shape => "Shape",
        WidgetType.HardwareName => "Hardware name",
        _ => "Widget"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
