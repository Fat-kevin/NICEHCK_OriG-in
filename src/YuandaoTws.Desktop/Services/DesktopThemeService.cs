using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace YuandaoTws.Desktop.Services;

/// <summary>同步 Windows 应用主题，并原地更新画刷，保证 StaticResource 也能实时换肤。</summary>
public sealed class DesktopThemeService
{
    private readonly DesktopPreferencesService _preferences;

    public DesktopThemeService(DesktopPreferencesService preferences)
    {
        _preferences = preferences;
        _preferences.PreferencesChanged += OnPreferencesChanged;
    }

    public bool IsDark { get; private set; }
    public event EventHandler? ThemeChanged;

    public void Apply()
    {
        var dark = _preferences.Current.ThemeMode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => ReadIsDark(),
        };
        var changed = dark != IsDark;
        IsDark = dark;
        var resources = System.Windows.Application.Current.Resources;

        // 深色模式使用“深蓝灰底 + 分层表面 + 高亮文字”，避免透明背板把所有层级冲成一片灰。
        SetBrush(resources, "WindowBackgroundBrush", dark ? "#E910141B" : "#EAF2F8FF");
        SetBrush(resources, "GlassShellBrush", dark ? "#E61B2430" : "#24FFFFFF");
        SetBrush(resources, "GlassPanelBrush", dark ? "#D52A3543" : "#72FFFFFF");
        SetBrush(resources, "GlassPanelStrongBrush", dark ? "#EB3E4D5E" : "#B8FFFFFF");
        SetBrush(resources, "GlassPanelSoftBrush", dark ? "#AC263441" : "#38FFFFFF");
        SetBrush(resources, "GlassPanelHoverBrush", dark ? "#DF4B6074" : "#A6FFFFFF");
        SetBrush(resources, "GlassBorderBrush", dark ? "#7891A5B8" : "#58FFFFFF");
        SetBrush(resources, "GlassBorderStrongBrush", dark ? "#B9D0DEE9" : "#88FFFFFF");
        SetBrush(resources, "TrackBrush", dark ? "#A0607388" : "#3A6C7E93");
        SetBrush(resources, "TextPrimaryBrush", dark ? "#F6F8FB" : "#152238");
        SetBrush(resources, "TextSecondaryBrush", dark ? "#D7E0EA" : "#52647A");
        SetBrush(resources, "TextMutedBrush", dark ? "#AAB8C6" : "#7D8DA1");
        SetBrush(resources, "TextDisabledBrush", dark ? "#7F90A0" : "#9BA9B8");
        SetBrush(resources, "AccentSoftBrush", dark ? "#75518BC0" : "#4C82B9E8");
        SetBrush(resources, "DividerBrush", dark ? "#708B9FB3" : "#48FFFFFF");

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

    private void OnPreferencesChanged(object? sender, EventArgs e) => Apply();

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
