using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PCInfoScreenStudio.ViewModels;
namespace PCInfoScreenStudio.Controls;
public partial class MatrixWorkspaceView : UserControl
{
    public MatrixWorkspaceView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>UpdatePreview(); Unloaded+=(_,_)=> { if(DataContext is MatrixWorkspaceViewModel vm)vm.SetPreviewActive(false); };
        IsVisibleChanged+=(_,_)=>UpdatePreview();
    }
    private void UpdatePreview() { if(DataContext is MatrixWorkspaceViewModel vm)vm.SetPreviewActive(IsLoaded && IsVisible); }
    private void SourceSearchChanged(object sender, TextChangedEventArgs e)
    { if (SensorSourcePicker is not null && IsLoaded) SensorSourcePicker.IsDropDownOpen = !string.IsNullOrWhiteSpace(SourceSearchBox.Text); }
    private void LayerFlagChanged(object sender, RoutedEventArgs e)
    { if (DataContext is MatrixWorkspaceViewModel vm && IsLoaded) { (sender as CheckBox)?.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource(); vm.LayerFlagsChanged(); } }
}
