using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PCInfoScreenStudio.Controls;

public enum NumericField
{
    None, Number, NonNegative, Size, Opacity, Minimum, Maximum, FontSize, GridSize,
    PhotoDuration, TransitionDuration, CaptionFontSize, OutlineThickness,
    Thickness, Segments, History, PlaybackSpeed, Fps, Transparency
}

/// <summary>Validates proposed text before model setters can silently clamp it.</summary>
public static class NumericValidation
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
        "Field", typeof(NumericField), typeof(NumericValidation),
        new PropertyMetadata(NumericField.None, OnFieldChanged));

    public static NumericField GetField(DependencyObject target) => (NumericField)target.GetValue(FieldProperty);
    public static void SetField(DependencyObject target, NumericField value) => target.SetValue(FieldProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FieldState), typeof(NumericValidation));

    private static void OnFieldChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TextBox box || box.GetValue(StateProperty) is FieldState) return;
        var state = new FieldState(box);
        box.SetValue(StateProperty, state);
        box.Loaded += state.OnLoaded;
        box.Unloaded += state.OnUnloaded;
        if (box.IsLoaded) state.OnLoaded(box, new RoutedEventArgs());
    }

    private sealed class FieldState(TextBox box)
    {
        private INotifyPropertyChanged? _model;
        private bool _attached;

        public void OnLoaded(object sender, RoutedEventArgs e)
        {
            var original = BindingOperations.GetBinding(box, TextBox.TextProperty);
            if (original is null) return;
            if (!original.ValidationRules.OfType<NumberRule>().Any())
            {
                // These fields use simple model bindings. Keep their source, formatting,
                // culture and commit-on-focus-loss behaviour when adding validation.
                var binding = new Binding
                {
                    Path = original.Path, Mode = original.Mode,
                    UpdateSourceTrigger = original.UpdateSourceTrigger,
                    Converter = original.Converter, ConverterParameter = original.ConverterParameter,
                    ConverterCulture = original.ConverterCulture, StringFormat = original.StringFormat,
                    FallbackValue = original.FallbackValue, TargetNullValue = original.TargetNullValue,
                    ValidatesOnExceptions = true, NotifyOnValidationError = true
                };
                if (original.Source is not null) binding.Source = original.Source;
                else if (original.RelativeSource is not null) binding.RelativeSource = original.RelativeSource;
                else if (!string.IsNullOrEmpty(original.ElementName)) binding.ElementName = original.ElementName;
                foreach (var rule in original.ValidationRules) binding.ValidationRules.Add(rule);
                binding.ValidationRules.Add(new NumberRule(box));
                BindingOperations.SetBinding(box, TextBox.TextProperty, binding);
            }
            if (!_attached)
            {
                box.TextChanged += OnTextChanged;
                box.DataContextChanged += OnDataContextChanged;
                _attached = true;
            }
            WatchModel();
        }

        public void OnUnloaded(object sender, RoutedEventArgs e)
        {
            box.TextChanged -= OnTextChanged;
            box.DataContextChanged -= OnDataContextChanged;
            _attached = false;
            UnwatchModel();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e) => ValidateDraft();
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => WatchModel();
        private void WatchModel()
        {
            UnwatchModel();
            _model = box.DataContext as INotifyPropertyChanged;
            if (_model is not null) PropertyChangedEventManager.AddHandler(_model, OnModelChanged, string.Empty);
        }
        private void UnwatchModel()
        {
            if (_model is not null) PropertyChangedEventManager.RemoveHandler(_model, OnModelChanged, string.Empty);
            _model = null;
        }
        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Models.WidgetModel.Minimum) or nameof(Models.WidgetModel.Maximum))
                ValidateDraft();
        }
        private void ValidateDraft() => box.GetBindingExpression(TextBox.TextProperty)?.ValidateWithoutUpdate();
    }

    private sealed class NumberRule(TextBox box) : ValidationRule(ValidationStep.RawProposedValue, false)
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (!double.TryParse(value?.ToString(), NumberStyles.Float, cultureInfo, out var number) || !double.IsFinite(number))
                return Invalid("Enter a finite number.");

            var field = GetField(box);
            var (minimum, maximum, integer, unit) = field switch
            {
                NumericField.NonNegative => (0d, double.PositiveInfinity, false, " px"),
                NumericField.Size => (8d, double.PositiveInfinity, false, " px"),
                NumericField.Opacity => (0d, 1d, false, ""),
                NumericField.FontSize => (6d, double.PositiveInfinity, false, " px"),
                NumericField.GridSize => (2d, 100d, true, " px"),
                NumericField.PhotoDuration => (1d, 3600d, false, " s"),
                NumericField.TransitionDuration => (0d, 10d, false, " s"),
                NumericField.CaptionFontSize => (6d, 100d, false, " px"),
                NumericField.OutlineThickness => (0d, 10d, false, " px"),
                NumericField.Thickness => (1d, double.PositiveInfinity, false, " px"),
                NumericField.Segments => (2d, 100d, true, ""),
                NumericField.History => (5d, 3600d, true, " s"),
                NumericField.PlaybackSpeed => (.1d, 4d, false, ""),
                NumericField.Fps => (0d, 60d, true, ""),
                NumericField.Transparency => (0d, 100d, false, "%"),
                _ => (double.NegativeInfinity, double.PositiveInfinity, false, "")
            };
            if (integer && number != Math.Truncate(number)) return Invalid("Enter a whole number.");
            if (number < minimum || number > maximum)
            {
                var message = double.IsPositiveInfinity(maximum)
                    ? $"At least {minimum.ToString(cultureInfo)}{unit}."
                    : $"{minimum.ToString(cultureInfo)} to {maximum.ToString(cultureInfo)}{unit}.";
                return Invalid(message);
            }
            if (box.DataContext is Models.WidgetModel widget)
            {
                if (field == NumericField.Minimum && number >= widget.Maximum)
                    return Invalid($"Less than Max ({widget.Maximum.ToString(cultureInfo)}).");
                if (field == NumericField.Maximum && number <= widget.Minimum)
                    return Invalid($"Greater than Min ({widget.Minimum.ToString(cultureInfo)}).");
            }
            return ValidationResult.ValidResult;
        }

        private static ValidationResult Invalid(string message) => new(false, message);
    }
}
