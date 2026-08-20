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

        // 深色模式使用“深底 + 明亮卡片 + 高对比文字”的三层结构，避免玻璃背景把文字压成一片。
        SetBrush(resources, "WindowBackgroundBrush", dark ? "#EC0B1016" : "#EAF2F8FF");
        SetBrush(resources, "GlassShellBrush", dark ? "#E0101721" : "#24FFFFFF");
        SetBrush(resources, "GlassSidebarBrush", dark ? "#D91B2734" : "#18FFFFFF");
        SetBrush(resources, "GlassPanelBrush", dark ? "#F02C3948" : "#72FFFFFF");
        SetBrush(resources, "GlassPanelStrongBrush", dark ? "#FA455463" : "#B8FFFFFF");
        SetBrush(resources, "GlassPanelSoftBrush", dark ? "#BF344350" : "#38FFFFFF");
        SetBrush(resources, "GlassPanelHoverBrush", dark ? "#F05A6A7A" : "#A6FFFFFF");
        SetBrush(resources, "GlassBorderBrush", dark ? "#A08A9EAF" : "#58FFFFFF");
        SetBrush(resources, "GlassBorderStrongBrush", dark ? "#E0D3DEE8" : "#88FFFFFF");
        SetBrush(resources, "TrackBrush", dark ? "#C08A9EAF" : "#3A6C7E93");
        SetBrush(resources, "TextPrimaryBrush", dark ? "#FFFFFF" : "#152238");
        SetBrush(resources, "TextSecondaryBrush", dark ? "#F1F6FB" : "#52647A");
        SetBrush(resources, "TextMutedBrush", dark ? "#CAD7E2" : "#7D8DA1");
        SetBrush(resources, "TextDisabledBrush", dark ? "#B0C0CD" : "#9BA9B8");
        SetBrush(resources, "AccentBrush", dark ? "#74C1FF" : "#287FD3");
        SetBrush(resources, "AccentHoverBrush", dark ? "#A3D6FF" : "#176BBE");
        SetBrush(resources, "AccentSoftBrush", dark ? "#8F4E9BD5" : "#4C82B9E8");
        SetBrush(resources, "SuccessBrush", dark ? "#6DE8AD" : "#1B9A67");
        SetBrush(resources, "WarningBrush", dark ? "#FFD16D" : "#D78322");
        SetBrush(resources, "DangerBrush", dark ? "#FF8290" : "#D94B5B");
        SetBrush(resources, "DividerBrush", dark ? "#B08DA2B5" : "#48FFFFFF");

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

        if (resources["CardShadow"] is System.Windows.Media.Effects.DropShadowEffect cardShadow && !cardShadow.IsFrozen)
        {
            cardShadow.Color = dark ? MediaColor.FromRgb(0x05, 0x0A, 0x10) : MediaColor.FromRgb(0x27, 0x44, 0x5D);
            cardShadow.Opacity = dark ? 0.30 : 0.10;
        }
        else
        {
            resources["CardShadow"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = dark ? 0.30 : 0.10,
                Color = dark ? MediaColor.FromRgb(0x05, 0x0A, 0x10) : MediaColor.FromRgb(0x27, 0x44, 0x5D),
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
            var raw = key?.GetValue("AppsUseLightTheme") ?? key?.GetValue("SystemUsesLightTheme");
            return raw switch
            {
                int value => value == 0,
                long value => value == 0,
                string value when int.TryParse(value, out var parsed) => parsed == 0,
                _ => false,
            };
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
