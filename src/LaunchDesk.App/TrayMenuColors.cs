using System.Drawing;
using System.Windows.Forms;

namespace LaunchDesk;

internal sealed class TrayMenuColors : ProfessionalColorTable
{
    private static Color Surface => Theme.IsDark ? Color.FromArgb(38, 41, 46) : Color.White;
    private static Color Border => Theme.IsDark ? Color.FromArgb(68, 74, 80) : Color.FromArgb(218, 224, 230);
    private static Color Selected => Theme.IsDark ? Color.FromArgb(44, 70, 61) : Color.FromArgb(226, 243, 236);
    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color MenuBorder => Border;
    public override Color SeparatorDark => Border;
    public override Color SeparatorLight => Surface;
    public override Color MenuItemSelected => Selected;
    public override Color MenuItemBorder => Selected;
    public override Color MenuItemSelectedGradientBegin => Selected;
    public override Color MenuItemSelectedGradientEnd => Selected;
}
