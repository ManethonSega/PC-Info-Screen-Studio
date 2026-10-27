using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PCInfoScreenStudio.ViewModels;
namespace PCInfoScreenStudio.Controls;
public partial class MatrixWorkspaceView : UserControl
{
    public MatrixWorkspaceView() => InitializeComponent();
    private void SourceSearchChanged(object sender, TextChangedEventArgs e)
    { if (SensorSourcePicker is not null && IsLoaded && DataContext is MatrixWorkspaceViewModel vm) SensorSourcePicker.IsDropDownOpen = !string.IsNullOrWhiteSpace(vm.SourceSearchText); }
    private void LayerFlagChanged(object sender, RoutedEventArgs e)
    { if (DataContext is MatrixWorkspaceViewModel vm && IsLoaded) { (sender as CheckBox)?.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource(); vm.LayerFlagsChanged(); } }
}
