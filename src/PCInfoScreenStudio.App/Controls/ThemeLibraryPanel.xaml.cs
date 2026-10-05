using System.Windows;
using System.Windows.Controls;

namespace PCInfoScreenStudio.Controls;

public partial class ThemeLibraryPanel : UserControl
{
    public ThemeLibraryPanel() => InitializeComponent();
    private void OnActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.DataContext = DataContext;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}
