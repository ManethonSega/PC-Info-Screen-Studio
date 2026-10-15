using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PCInfoScreenStudio.Controls;

internal sealed class WorkspaceWidthGrip(WorkspaceDock dock) : Thumb
{
    internal WorkspaceDock Dock { get; } = dock;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 40 : 10;
            var delta = (e.Key == Key.Right ? step : -step) * (Dock.RailOnRight ? -1 : 1);
            Dock.ResizePanel(e.Key == Key.Home ? 240 : e.Key == Key.End ? 460 : Dock.EffectivePanelWidth + delta, true);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WidthPeer(this);

    private sealed class WidthPeer(WorkspaceWidthGrip owner) : ThumbAutomationPeer(owner), IRangeValueProvider
    {
        public override object? GetPattern(PatternInterface patternInterface)
            => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 40;
        public double SmallChange => 10;
        public double Maximum => 460;
        public double Minimum => 240;
        public double Value => owner.Dock.PreferredPanelWidth;
        public void SetValue(double value)
        {
            if (!owner.IsEnabled) throw new System.Windows.Automation.ElementNotEnabledException();
            if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            owner.Dock.ResizePanel(value, true);
        }
    }
}
