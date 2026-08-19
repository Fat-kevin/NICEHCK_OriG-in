using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace YuandaoTws.Desktop.Services;

/// <summary>同步 Windows 应用主题，并原地更新画刷，保证 StaticResource 也能实时换肤。</summary>
public sealed class DesktopThemeService
{
    public bool IsDark { get; private set; }
    public event EventHandler? ThemeChanged;

    public void Apply()
    {
        var dark = ReadIsDark();
        var changed = dark != IsDark;
        IsDark = dark;
        var resources = System.Windows.Application.Current.Resources;

        SetBrush(resources, "WindowBackgroundBrush", dark ? "#E91A2029" : "#EAF2F8FF");
        SetBrush(resources, "GlassShellBrush", dark ? "#D91A2028" : "#24FFFFFF");
        SetBrush(resources, "GlassPanelBrush", dark ? "#9A263242" : "#72FFFFFF");
        SetBrush(resources, "GlassPanelStrongBrush", dark ? "#D13A4858" : "#B8FFFFFF");
        SetBrush(resources, "GlassPanelSoftBrush", dark ? "#60374756" : "#38FFFFFF");
        SetBrush(resources, "GlassPanelHoverBrush", dark ? "#B14A5A6C" : "#A6FFFFFF");
        SetBrush(resources, "GlassBorderBrush", dark ? "#557D91A5" : "#58FFFFFF");
        SetBrush(resources, "GlassBorderStrongBrush", dark ? "#8294A6B8" : "#88FFFFFF");
        SetBrush(resources, "TrackBrush", dark ? "#5A788697" : "#3A6C7E93");
        SetBrush(resources, "TextPrimaryBrush", dark ? "#F2F6FA" : "#152238");
        SetBrush(resources, "TextSecondaryBrush", dark ? "#C1CFDC" : "#52647A");
        SetBrush(resources, "TextMutedBrush", dark ? "#9EAFBF" : "#7D8DA1");
        SetBrush(resources, "TextDisabledBrush", dark ? "#778999" : "#9BA9B8");
        SetBrush(resources, "AccentSoftBrush", dark ? "#48618AB0" : "#4C82B9E8");
        SetBrush(resources, "DividerBrush", dark ? "#3F8191A3" : "#48FFFFFF");

        if (resources["FloatingShadow"] is System.Windows.Media.Effects.DropShadowEffect shadow && !shadow.IsFrozen)
        {
            shadow.Color = dark ? MediaColor.FromRgb(0, 0, 0) : MediaColor.FromRgb(0x22, 0x41, 0x5D);
            shadow.Opacity = dark ? 0.38 : 0.22;
        }
        else
        {
            resources["FloatingShadow"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 7,
                Direction = 270,
                Opacity = dark ? 0.38 : 0.22,
                Color = dark ? MediaColor.FromRgb(0, 0, 0) : MediaColor.FromRgb(0x22, 0x41, 0x5D),
            };
        }

        if (changed)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex)
    {
        var color = BatteryColorResolver.Parse(hex, Colors.Transparent);
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }
}
