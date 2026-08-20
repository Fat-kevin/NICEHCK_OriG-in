using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 为普通 WPF HWND 启用真正的窗口后方模糊：后面的其它应用内容会进入 DWM
/// 合成结果，而不是只对桌面壁纸做取色。Windows 10/11 使用原生 Composition
/// Windows 10/11 使用稳定的原生 Acrylic，Windows 7/旧 DWM 使用 DwmEnableBlurBehindWindow。
/// </summary>
public sealed class WindowBackdropService : IDisposable
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmcpRound = 2;
    private const int DwmcpDoNotRound = 1;
    private const uint DwmBbEnable = 0x1;

    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private byte _opacity = 0x72;
    private bool _isDark;
    private readonly NativeCompositionBackdropService _composition = new();
    private const double WindowCornerRadius = 26;

    public void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // WPF 自己不能留下不透明的合成目标，否则 DWM 后方内容会被整块盖住。
        window.Background = new SolidColorBrush(Colors.Transparent);
        if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }

        // 让 DWM 和 WPF 使用同一个圆角窗口区域，避免出现“外层方框 + 内层圆角 Border”的双重轮廓。
        window.SizeChanged += OnWindowSizeChanged;
        window.Closed += OnWindowClosed;
        ApplyRoundedWindowRegion(window);
        var round = UsesDwmRoundedCorners ? DwmcpRound : DwmcpDoNotRound;
        _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
        ApplyTheme(window, IsSystemDarkMode());

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            // 优先使用真正的 Host Backdrop + GPU Gaussian Blur。它采样的是窗口后方
            // 的其它应用内容，不是桌面壁纸；失败时再降级到 Accent Acrylic。
            if (_composition.TryApply(window, _opacity))
            {
                ApplyRoundedWindowRegion(window);
                return;
            }

            // Win10/11 的无边框 WPF HWND 使用 Acrylic 更稳定。直接使用纯 Blur Behind
            // 会在窗口移动重组期间产生黑色回退和闪烁；纯 DWM Blur 仅保留给 Win7。
            if (!SetAccentPolicy(handle, AccentEnableAcrylicBlurBehind, _opacity))
            {
                _ = SetAccentPolicy(handle, AccentEnableBlurBehind, _opacity);
            }

            ApplyRoundedWindowRegion(window);
            return;
        }

        // Windows 7 / 旧版 DWM：扩展玻璃到完整客户区，并开启后方模糊。
        var margins = new Margins(-1, -1, -1, -1);
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
        var blur = new DwmBlurBehind
        {
            Flags = DwmBbEnable,
            Enable = true,
            BlurRegion = IntPtr.Zero,
            TransitionOnMaximized = false,
        };
        _ = DwmEnableBlurBehindWindow(handle, ref blur);
    }

    public void ApplyTheme(Window window, bool isDark)
    {
        _isDark = isDark;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        if (_composition.IsActive)
        {
            _composition.SetOpacity(_opacity);
        }
        else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            if (!SetAccentPolicy(handle, AccentEnableAcrylicBlurBehind, _opacity))
            {
                _ = SetAccentPolicy(handle, AccentEnableBlurBehind, _opacity);
            }
        }
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
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

    private static bool UsesDwmRoundedCorners => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Window window)
        {
            ApplyRoundedWindowRegion(window);
        }
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                _ = SetWindowRgn(handle, IntPtr.Zero, true);
            }
        }
    }

    private void ApplyRoundedWindowRegion(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dpi = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var widthDip = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var heightDip = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        if (widthDip <= 0 || heightDip <= 0)
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Round(widthDip * dpi.M11));
        var height = Math.Max(1, (int)Math.Round(heightDip * dpi.M22));
        var radius = Math.Max(2, (int)Math.Round(WindowCornerRadius * Math.Max(dpi.M11, dpi.M22)));

        // Windows 11 的 DWM 圆角会在 Composition 合成之后裁剪最终窗口。
        // 不再额外设置 HRGN，避免 Composition/DWM/WPF 三套边界叠加。
        if (UsesDwmRoundedCorners)
        {
            if (_composition.IsActive)
            {
                _composition.UpdateClip(widthDip, heightDip, WindowCornerRadius);
            }

            return;
        }

        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        var regionResult = region == IntPtr.Zero ? 0 : SetWindowRgn(handle, region, true);
        if (region != IntPtr.Zero && regionResult == 0)
        {
            DeleteObject(region);
        }

        if (_composition.IsActive)
        {
            _composition.UpdateClip(widthDip, heightDip, WindowCornerRadius);
        }
    }

    /// <summary>实时调整后方 Acrylic 层的前景透明度，范围 0–100。</summary>
    public void SetOpacity(Window window, double percent)
    {
        _opacity = (byte)Math.Clamp(Math.Round(percent / 100d * 255d), 0, 255);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            return;
        }

        if (_composition.IsActive)
        {
            _composition.SetOpacity(_opacity);
            return;
        }

        if (!SetAccentPolicy(handle, AccentEnableAcrylicBlurBehind, _opacity))
        {
            _ = SetAccentPolicy(handle, AccentEnableBlurBehind, _opacity);
        }
    }

    private bool SetAccentPolicy(IntPtr handle, int accentState, byte opacity)
    {
        var policy = new AccentPolicy
        {
            State = accentState,
            // 2 保留系统边缘/阴影行为，不生成额外的 WPF 外框。
            Flags = 2,
            // ARGB：低 alpha 让后方应用内容可见，WPF 面板再负责文本可读性。
            GradientColor = (opacity << 24) | (_isDark ? 0x202A35 : 0xEEF5FB),
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var policyPointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;

        public Margins(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr BlurRegion;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    public void Dispose() => _composition.Dispose();
}
