using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace PCInfoScreenStudio.Controls;

/// <summary>A filtered list must not erase a widget's source when it hides the current choice.</summary>
public sealed class SourcePicker : ComboBox
{
    public static readonly DependencyProperty SelectedSourceProperty = DependencyProperty.Register(
        nameof(SelectedSource), typeof(string), typeof(SourcePicker),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (owner, _) => ((SourcePicker)owner).SynchronizeSelection()));

    private bool _synchronizing;

    public string SelectedSource
    {
        get => (string)GetValue(SelectedSourceProperty);
        set => SetValue(SelectedSourceProperty, value);
    }

    public SourcePicker()
    {
        IsEditable = true;
        IsReadOnly = true;
        IsTextSearchEnabled = false;
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        // Selector can clear SelectedItem during filtering. Keep that out of the source binding.
        var previous = _synchronizing;
        _synchronizing = true;
        try { base.OnItemsChanged(e); }
        finally { _synchronizing = previous; }
        SynchronizeSelection();
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (!_synchronizing && SelectedItem is string source)
            SetCurrentValue(SelectedSourceProperty, source);
        SetCurrentValue(TextProperty, SelectedSource ?? string.Empty);
    }

    private void SynchronizeSelection()
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            SetCurrentValue(SelectedItemProperty, Items.Contains(SelectedSource) ? SelectedSource : null);
            SetCurrentValue(TextProperty, SelectedSource ?? string.Empty);
        }
        finally { _synchronizing = false; }
    }
}
