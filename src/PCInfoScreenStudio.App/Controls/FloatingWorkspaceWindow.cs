using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using WinForms = System.Windows.Forms;

namespace PCInfoScreenStudio.Controls;

/// <summary>An owned tool window. Native screen coordinates keep recovery independent of monitor origin.</summary>
internal sealed class FloatingWorkspaceWindow : Window
{
    private readonly WorkspacePanel _panel;
    private readonly Action _changed;
    private readonly Func<double> _minimumHeight;
    private readonly DispatcherTimer _saveTimer;
    private bool _detaching;
    private bool _placing;

    internal FloatingWorkspaceWindow(Window owner, WorkspacePanel panel, Border host, Action changed, Func<double> minimumHeight)
    {
        Owner = owner; _panel = panel; _changed = changed; _minimumHeight = minimumHeight;
        SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = owner });
        foreach (var binding in owner.InputBindings.OfType<KeyBinding>())
            InputBindings.Add(new KeyBinding(binding.Command, binding.Key, binding.Modifiers)
                { CommandParameter = binding.CommandParameter, CommandTarget = binding.CommandTarget });
        Title = panel.Title; Content = host; ShowInTaskbar = false; ShowActivated = false;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 260; MinHeight = 120; Width = panel.FloatingBounds?.Width ?? 340;
        MaxHeight = Math.Max(120, SystemParameters.WorkArea.Height);
        if (panel.FloatingBounds is { } bounds) Height = bounds.Height;
        else SizeToContent = SizeToContent.Height;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = owner });
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveBounds(); _changed(); };
        LocationChanged += (_, _) => QueueSave(); SizeChanged += (_, _) => QueueSave();
        Closing += OnClosing;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_detaching && IsVisible && Content is not null)
            MinHeight = Math.Min(MaxHeight, Math.Max(120, _minimumHeight() + 40));
    }

    internal void ShowRecovered()
    {
        _placing = true;
        try
        {
            Show(); UpdateLayout();
            var saved = _panel.FloatingBounds;
            var point = saved is not null ? new Point(saved.ScreenX, saved.ScreenY) : Owner.PointToScreen(new Point(140, 110));
            PlaceOnScreen(point);
        }
        finally { _placing = false; }
        SaveBounds();
    }

    internal void PlaceOnScreen(Point point)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var work = WinForms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).WorkingArea;
        var width = Math.Min(bounds.Right - bounds.Left, work.Width);
        var height = Math.Min(bounds.Bottom - bounds.Top, work.Height);
        var x = (int)Math.Clamp(point.X, work.Left, work.Right - width);
        var y = (int)Math.Clamp(point.Y, work.Top, work.Bottom - height);
        // Position and clamp in device pixels, including negative-origin secondary monitors.
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0014); // NOZORDER | NOACTIVATE
        SaveBounds();
    }

    internal void SaveBounds()
    {
        if (_detaching || !IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        _panel.FloatingBounds = new FloatingPanelBounds { ScreenX = bounds.Left, ScreenY = bounds.Top,
            Width = ActualWidth, Height = ActualHeight };
    }

    private void QueueSave()
    { if (!_placing && !_detaching && IsVisible) { SaveBounds(); _saveTimer.Stop(); _saveTimer.Start(); } }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_detaching) return;
        e.Cancel = true; _saveTimer.Stop(); SaveBounds();
        _panel.IsOpen = false; Hide(); _changed();
    }

    internal void DetachAndClose()
    {
        _saveTimer.Stop(); SaveBounds(); _detaching = true; LayoutUpdated -= OnLayoutUpdated; Content = null; Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
