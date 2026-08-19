using System.Drawing;

namespace YuandaoTws.Desktop.Services;

public sealed class WindowsColorPickerService
{
    public string? Pick(string initialHex)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = ToDrawingColor(initialHex),
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"
            : null;
    }

    private static Color ToDrawingColor(string hex)
    {
        var color = BatteryColorResolver.Parse(hex, System.Windows.Media.Colors.DodgerBlue);
        return Color.FromArgb(color.R, color.G, color.B);
    }
}
