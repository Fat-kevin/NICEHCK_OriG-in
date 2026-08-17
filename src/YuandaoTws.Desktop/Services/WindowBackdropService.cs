using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YuandaoTws.Desktop.Services;

/// <summary>为普通 WPF HWND 启用 Windows 11 系统背板与圆角。</summary>
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

        var roundedCorners = DwmcpRound;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref roundedCorners, sizeof(int));

        var darkMode = IsSystemDarkMode() ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var mica = DwmsbtMainWindow;
            _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref mica, sizeof(int));
        }
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
