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
        bool rightCharging)
    {
        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;

            DrawBattery(graphics, new Rectangle(3, 7, 11, 19), leftBattery, leftCharging, isConnected);
            DrawBattery(graphics, new Rectangle(18, 7, 11, 19), rightBattery, rightCharging, isConnected);
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
        bool isConnected)
    {
        var outlineColor = isConnected ? Color.FromArgb(235, 236, 242) : Color.FromArgb(145, 153, 165);
        var fillColor = isConnected ? BatteryColor(battery) : Color.FromArgb(105, 115, 128);

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
            using var boltPen = new Pen(Color.FromArgb(80, 229, 160), 1.6f)
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

    private static Color BatteryColor(double battery) => battery switch
    {
        <= 15 => Color.FromArgb(235, 92, 92),
        <= 35 => Color.FromArgb(241, 172, 74),
        _ => Color.FromArgb(70, 168, 236),
    };

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
