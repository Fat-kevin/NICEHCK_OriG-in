using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 生成任务栏和通知区域使用的双耳电量图标。
/// 图标只承载快速视觉状态，精确百分比通过通知区域提示文本提供。
/// </summary>
internal static class BatteryStatusIconFactory
{
    private const int IconSize = 32;

    public static IntPtr Create(
        bool isConnected,
        double leftBattery,
        double rightBattery,
        bool leftCharging,
        bool rightCharging,
        DesktopPreferences preferences,
        bool isDark)
    {
        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;

            DrawBattery(graphics, new Rectangle(3, 6, 11, 20), leftBattery, leftCharging, isConnected, preferences, isDark);
            DrawBattery(graphics, new Rectangle(18, 6, 11, 20), rightBattery, rightCharging, isConnected, preferences, isDark);
        }

        return bitmap.GetHicon();
    }

    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero)
        {
            _ = DestroyIcon(icon);
        }
    }

    private static void DrawBattery(
        Graphics graphics,
        Rectangle bounds,
        double battery,
        bool charging,
        bool isConnected,
        DesktopPreferences preferences,
        bool isDark)
    {
        var outlineColor = isConnected
            ? (isDark ? Color.FromArgb(240, 242, 247) : Color.FromArgb(95, 109, 123))
            : Color.FromArgb(125, 135, 147);
        var fillColor = ToDrawing(BatteryColorResolver.Resolve(
            isConnected ? battery : null,
            isConnected,
            charging,
            preferences));

        using var outlinePen = new Pen(outlineColor, 1.6f);
        using var outlinePath = RoundedPath(bounds, 3);
        graphics.DrawPath(outlinePen, outlinePath);

        var percent = isConnected ? Math.Clamp(battery, 0, 100) : 0;
        var fillHeight = percent <= 0 ? 0 : Math.Max(2, (int)Math.Round((bounds.Height - 4) * percent / 100d));
        if (fillHeight > 0)
        {
            var fillBounds = new Rectangle(
                bounds.Left + 2,
                bounds.Bottom - 2 - fillHeight,
                bounds.Width - 4,
                fillHeight);
            using var fillBrush = new SolidBrush(fillColor);
            graphics.FillRectangle(fillBrush, fillBounds);
        }

        using var capBrush = new SolidBrush(outlineColor);
        graphics.FillRectangle(capBrush, bounds.Left + 4, bounds.Top - 2, bounds.Width - 8, 2);

        if (charging && isConnected)
        {
            using var boltPen = new Pen(ToDrawing(BatteryColorResolver.Parse(preferences.ChargingColor, BatteryColorResolver.LowColor)), 1.6f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            var x = bounds.Left + bounds.Width / 2f;
            graphics.DrawLines(boltPen, new[]
            {
                new PointF(x + 1, bounds.Top + 5),
                new PointF(x - 2, bounds.Top + 11),
                new PointF(x + 1, bounds.Top + 11),
                new PointF(x - 2, bounds.Top + 16),
            });
        }
    }

    private static Color ToDrawing(System.Windows.Media.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);
}
