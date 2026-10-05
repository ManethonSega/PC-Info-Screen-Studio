using System.Drawing;
using System.Windows.Forms;

namespace PCInfoScreenStudio.Services;

/// <summary>Keep tray menu text readable under light or dark Windows settings.</summary>
public sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayColors()) { }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.FromArgb(240, 242, 245) : Color.FromArgb(146, 156, 169);
        base.OnRenderItemText(e);
    }
    private sealed class TrayColors : ProfessionalColorTable
    {
        public TrayColors() { UseSystemColors = false; }
        private static Color Surface => Color.FromArgb(23, 26, 31);
        private static Color Selected => Color.FromArgb(49, 69, 99);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemSelectedGradientBegin => Selected;
        public override Color MenuItemSelectedGradientEnd => Selected;
        public override Color MenuItemPressedGradientBegin => Selected;
        public override Color MenuItemPressedGradientMiddle => Selected;
        public override Color MenuItemPressedGradientEnd => Selected;
        public override Color MenuItemBorder => Color.FromArgb(93, 123, 168);
        public override Color MenuBorder => Color.FromArgb(52, 58, 69);
        public override Color SeparatorDark => Color.FromArgb(52, 58, 69);
        public override Color SeparatorLight => Surface;
    }
}
