using System.Globalization;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace YuandaoTws.Desktop.Services;

public static class BatteryColorResolver
{
    public static readonly MediaColor LowColor = MediaColor.FromRgb(0xE9, 0x5B, 0x5B);
    public static readonly MediaColor MediumColor = MediaColor.FromRgb(0xE6, 0x9B, 0x3F);
    public static readonly MediaColor UnknownColor = MediaColor.FromRgb(0x9A, 0xA4, 0xAF);
    public static readonly MediaColor DisconnectedColor = MediaColor.FromRgb(0x74, 0x7D, 0x89);

    public static MediaColor Resolve(double? percent, bool connected, bool charging, DesktopPreferences preferences)
    {
        if (!connected || percent is null)
        {
            return DisconnectedColor;
        }

        if (charging)
        {
            return Parse(preferences.ChargingColor, MediaColor.FromRgb(0x50, 0xE5, 0xA0));
        }

        return percent.Value switch
        {
            <= 15 => LowColor,
            <= 35 => MediumColor,
            _ => Parse(preferences.BatteryAccentColor, MediaColor.FromRgb(0x46, 0xA8, 0xEC)),
        };
    }

    public static string NormalizeHex(string? value, string fallback)
    {
        try
        {
            var color = Parse(value, Parse(fallback, Colors.Transparent));
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static MediaColor Parse(string? value, MediaColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return MediaColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        if (text.Length == 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return MediaColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        return fallback;
    }

    public static SolidColorBrush Brush(string? value, MediaColor fallback)
    {
        var brush = new SolidColorBrush(Parse(value, fallback));
        brush.Freeze();
        return brush;
    }
}
