using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YuandaoTws.Desktop.Services;

/// <summary>为普通 WPF HWND 启用 Windows 11 系统背板（Mica）与圆角。</summary>
public sealed class WindowBackdropService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmcpRound = 2;
    private const int DwmsbtMainWindow = 2;

    public void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Windows 11 22H2+：启用 Mica 背板。背板要透出来，窗口背景与 WPF 组合目标都必须透明——
        // 仅改窗口背景色（哪怕半透明）不够，CompositionTarget 不透明会把 DWM 背板整个盖住。
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            window.Background = new SolidColorBrush(Colors.Transparent);
            if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
            {
                target.BackgroundColor = Colors.Transparent;
            }

            var mica = DwmsbtMainWindow;
            _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref mica, sizeof(int));
        }

        var roundedCorners = DwmcpRound;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref roundedCorners, sizeof(int));

        var darkMode = IsSystemDarkMode() ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
