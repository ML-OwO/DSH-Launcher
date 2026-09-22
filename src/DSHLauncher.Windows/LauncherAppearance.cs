using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

#nullable enable

namespace DSHLauncher;

internal static class LauncherAppearance
{
    public static readonly Font Regular = CreateRegularFont();
    public static readonly Font Medium = CreateMediumFont();
    public static readonly Font StatusBold = new(Regular.FontFamily, 10F, FontStyle.Bold);

    private static Font CreateMediumFont()
    {
        using var installed = new InstalledFontCollection();
        foreach (string name in new[] { "思源黑体 Medium", "Source Han Sans SC Medium", "Source Han Sans CN Medium", "思源黑体 CN Medium" })
        {
            FontFamily? family = installed.Families.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (family is not null && family.IsStyleAvailable(FontStyle.Regular))
                return new Font(family.Name, 10F, FontStyle.Regular, GraphicsUnit.Point);
        }
        return new Font(Regular.FontFamily, 10F, FontStyle.Regular);
    }

    private static Font CreateRegularFont()
    {
        using var installed = new InstalledFontCollection();
        // Exact family matching avoids the distinct Normal/Light/Medium faces.
        // Use installed fonts by name so GDI TextRenderer and native controls agree.
        foreach (string name in new[] { "思源黑体", "Source Han Sans SC", "Source Han Sans CN", "思源黑体 CN", "Microsoft YaHei UI" })
        {
            FontFamily? family = installed.Families.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (family is not null && family.IsStyleAvailable(FontStyle.Regular))
                return new Font(family.Name, 10F, FontStyle.Regular, GraphicsUnit.Point);
        }
        return new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 10F, FontStyle.Regular);
    }
}

internal class LauncherMenuItem : ToolStripMenuItem
{
    public LauncherMenuItem(string text, EventHandler? handler = null) : base(text, null, handler)
    {
        Font = LauncherAppearance.Medium;
        Padding = new Padding(12, 4, 12, 4);
        Margin = new Padding(2, 0, 2, 0);
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        Size size = base.GetPreferredSize(constrainingSize);
        float scale = (Owner?.DeviceDpi ?? 96) / 96F;
        // Submenu parents and ordinary actions must have identical row heights.
        size.Width += (int)Math.Round(8 * scale);
        size.Height = Math.Max(Font.Height + (int)Math.Round(8 * scale), (int)Math.Round(30 * scale));
        return size;
    }
}
