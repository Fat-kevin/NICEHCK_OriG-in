using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace YuandaoTws.Desktop.Services;

public readonly record struct NativeTaskbarPalette(Color Accent, Color Charging, bool IsDark);

/// <summary>原生 GDI+ 任务栏状态胶囊。它是独立 HWND，但不置顶、不改任务栏工作区、不抢焦点。</summary>
public sealed class NativeTaskbarBatteryWindow : IDisposable
{
    private const string WindowClassName = "YuandaoTws.NativeTaskbarBatteryWindow";
    private const int BaseWidth = 150;
    private const int BaseHeight = 32;
    private const int BaseRadius = 9;
    private const int HorizontalOffset = 235;
    private const uint TimerId = 1;
    private const uint TimerIntervalMs = 2000;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoSendChanging = 0x0400;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShownoactivate = 4;
    private const uint WmNccreate = 0x0081;
    private const uint WmNcdestroy = 0x0082;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmTimer = 0x0113;
    private const uint WmLbuttonup = 0x0202;
    private const uint WmMouseactivate = 0x0021;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmDisplaychange = 0x007E;
    private const uint WmSettingchange = 0x001A;
    private const int HtClient = 1;
    private const int MaNoactivate = 3;
    private const int GwlUserdata = -21;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly WndProcDelegate WindowProcDelegate = WindowProc;
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private static ushort _windowClassAtom;

    private readonly Action _showMainWindow;
    private readonly DispatcherTimer _hostWatchTimer;
    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private GCHandle _selfHandle;
    private NativeTaskbarPalette _palette;
    private int _dpi = 96;
    private int _width = BaseWidth;
    private int _height = BaseHeight;
    private bool _isConnected;
    private bool _isSearching;
    private bool _leftCharging;
    private bool _rightCharging;
    private int _leftBattery;
    private int _rightBattery;
    private bool _disposed;

    public NativeTaskbarBatteryWindow(Action showMainWindow, Dispatcher dispatcher, NativeTaskbarPalette palette)
    {
        _showMainWindow = showMainWindow;
        _palette = palette;
        _hostWatchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(TimerIntervalMs), DispatcherPriority.Background, (_, _) => MaintainTaskbarHost(), dispatcher);
    }

    public bool Show()
    {
        if (_disposed) return false;
        _hostWatchTimer.Start();
        EnsureCreated();
        if (_hwnd == IntPtr.Zero) return false;
        ShowWindow(_hwnd, SwShownoactivate);
        EnsureDpi();
        PositionOnTaskbar(false);
        return true;
    }

    public void UpdateStatus(bool isConnected, bool isSearching, double leftBattery, double rightBattery, bool leftCharging = false, bool rightCharging = false)
    {
        _isConnected = isConnected;
        _isSearching = isSearching;
        _leftBattery = isConnected ? ClampBattery(leftBattery) : 0;
        _rightBattery = isConnected ? ClampBattery(rightBattery) : 0;
        _leftCharging = isConnected && leftCharging;
        _rightCharging = isConnected && rightCharging;
        if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void UpdatePalette(NativeTaskbarPalette palette)
    {
        _palette = palette;
        if (_hwnd != IntPtr.Zero)
        {
            ApplyNativeAppearance();
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void EnsureCreated()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (_hwnd != IntPtr.Zero && (_taskbarHwnd == taskbar || taskbar == IntPtr.Zero)) return;
        DestroyNativeWindow();
        _taskbarHwnd = taskbar;
        RegisterWindowClass();
        _selfHandle = GCHandle.Alloc(this);
        // 以 Explorer 任务栏作为 owner，让窗口跟随任务栏的 Z 序，但不使用全局置顶。
        _hwnd = CreateWindowEx(WsExToolWindow | WsExNoActivate | WsExLayered, WindowClassName, "原点耳机电量", WsPopup | WsVisible, 0, 0, _width, _height, _taskbarHwnd, IntPtr.Zero, GetModuleHandle(null), GCHandle.ToIntPtr(_selfHandle));
        if (_hwnd == IntPtr.Zero) { _selfHandle.Free(); return; }
        ApplyNativeAppearance();
        SetTimer(_hwnd, TimerId, TimerIntervalMs, IntPtr.Zero);
    }

    private void MaintainTaskbarHost()
    {
        if (_disposed) return;
        EnsureCreated();
        if (_hwnd != IntPtr.Zero) { RestoreVisibility(); PositionOnTaskbar(false); }
    }

    private void RestoreVisibility()
    {
        if (_hwnd == IntPtr.Zero) return;
        _ = SetWindowPos(_hwnd, HwndTop, 0, 0, 0, 0, SwpNoActivate | SwpNoMove | SwpNoSize | SwpNoSendChanging | SwpNoOwnerZOrder | SwpShowWindow);
    }

    private void RegisterWindowClass()
    {
        if (_windowClassAtom != 0) return;
        var windowClass = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), Style = 0x0002, WindowProc = WindowProcDelegate, Instance = GetModuleHandle(null), Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)), ClassName = WindowClassName };
        _windowClassAtom = RegisterClassEx(ref windowClass);
        if (_windowClassAtom == 0 && Marshal.GetLastWin32Error() != 1410) _windowClassAtom = 1;
    }

    private void ApplyNativeAppearance()
    {
        if (_hwnd == IntPtr.Zero) return;
        var dark = _palette.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(_hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        _ = SetLayeredWindowAttributes(_hwnd, 0, (byte)(_palette.IsDark ? 224 : 238), LwaAlpha);
        var corner = DwmCornerRound;
        _ = DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        ApplyWindowRegion();
    }

    private void EnsureDpi()
    {
        var dpi = _hwnd == IntPtr.Zero ? 96 : GetDpiForWindow(_hwnd);
        if (dpi == 0 || dpi == _dpi) return;
        _dpi = (int)dpi; _width = Scale(BaseWidth); _height = Scale(BaseHeight); ApplyWindowRegion();
    }

    private void PositionOnTaskbar(bool force)
    {
        if (_hwnd == IntPtr.Zero) return;
        EnsureDpi();
        var taskbar = _taskbarHwnd != IntPtr.Zero && GetWindowRect(_taskbarHwnd, out var rect) ? rect : new NativeRect { Left = 0, Top = 0, Right = GetSystemMetrics(SmCxScreen), Bottom = GetSystemMetrics(SmCyScreen) };
        var horizontal = taskbar.Width >= taskbar.Height * 2;
        int x, y;
        if (horizontal)
        {
            var minimum = Scale(8);
            var maximum = Math.Max(minimum, taskbar.Width - _width - Scale(8));
            x = taskbar.Left + Math.Clamp(Scale(HorizontalOffset), minimum, maximum);
            y = taskbar.Top + Math.Max(0, (taskbar.Height - _height) / 2);
        }
        else
        {
            x = taskbar.Left + Math.Max(0, (taskbar.Width - _width) / 2);
            y = taskbar.Top + Math.Max(0, (taskbar.Height - _height) / 2);
        }
        if (!force && GetWindowRect(_hwnd, out var current) && current.Left == x && current.Top == y && current.Width == _width && current.Height == _height)
        {
            if (!IsWindowVisible(_hwnd)) _ = ShowWindow(_hwnd, SwShownoactivate);
            return;
        }
        _ = SetWindowPos(_hwnd, HwndTop, x, y, _width, _height, SwpNoActivate | SwpNoSendChanging | SwpNoOwnerZOrder | SwpShowWindow);
    }

    private void ApplyWindowRegion()
    {
        if (_hwnd == IntPtr.Zero) return;
        var radius = Scale(BaseRadius);
        var region = CreateRoundRectRgn(0, 0, _width + 1, _height + 1, radius * 2, radius * 2);
        if (region != IntPtr.Zero && SetWindowRgn(_hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void Paint(IntPtr hdc)
    {
        if (!GetClientRect(_hwnd, out var client)) return;
        using var graphics = Graphics.FromHdc(hdc);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(_palette.IsDark ? Color.FromArgb(232, 27, 34, 45) : Color.FromArgb(236, 249, 251, 253));
        var bounds = new Rectangle(0, 0, client.Width - 1, client.Height - 1);
        using var border = new Pen(_palette.IsDark ? Color.FromArgb(110, 154, 173, 191) : Color.FromArgb(145, 169, 182, 194), Math.Max(1, Scale(1)));
        using var path = RoundedPath(bounds, Scale(BaseRadius));
        graphics.DrawPath(border, path);

        using var textBrush = new SolidBrush(_palette.IsDark ? Color.FromArgb(235, 243, 248) : Color.FromArgb(42, 57, 72));
        using var mutedBrush = new SolidBrush(_palette.IsDark ? Color.FromArgb(178, 192, 204) : Color.FromArgb(89, 105, 120));
        using var normalFont = new Font("Microsoft YaHei UI", Math.Max(8, Scale(9)), FontStyle.Regular, GraphicsUnit.Pixel);
        using var boldFont = new Font("Microsoft YaHei UI", Math.Max(9, Scale(10)), FontStyle.Bold, GraphicsUnit.Pixel);
        if (!_isConnected)
        {
            graphics.DrawString(_isSearching ? "正在连接" : "等待连接", normalFont, mutedBrush, Scale(10), Scale(7));
            return;
        }

        // 以实际可视内容为基准居中；百分比文字比左侧电池轮廓占用更多宽度。
        DrawBattery(graphics, Scale(20), _leftBattery, "左", _leftCharging, textBrush, mutedBrush, boldFont, normalFont);
        DrawBattery(graphics, Scale(87), _rightBattery, "右", _rightCharging, textBrush, mutedBrush, boldFont, normalFont);
    }

    private void DrawBattery(Graphics graphics, int x, int percent, string label, bool charging, Brush textBrush, Brush mutedBrush, Font boldFont, Font normalFont)
    {
        var y = Scale(4);
        var battery = new Rectangle(x, Scale(9), Scale(10), Scale(16));
        using var outline = new Pen(_palette.IsDark ? Color.FromArgb(215, 221, 230, 237) : Color.FromArgb(115, 128, 143, 157), Math.Max(1, Scale(1)));
        graphics.DrawRoundedRectangle(outline, battery, Scale(2));
        using var cap = new SolidBrush(outline.Color);
        graphics.FillRectangle(cap, x + Scale(3), Scale(7), Scale(4), Scale(2));
        var fillColor = charging ? _palette.Charging : percent <= 15 ? Color.FromArgb(233, 91, 91) : percent <= 35 ? Color.FromArgb(230, 155, 63) : _palette.Accent;
        if (percent > 0)
        {
            using var fill = new SolidBrush(fillColor);
            var fillHeight = Math.Max(2, (int)Math.Round(Scale(12) * percent / 100d));
            graphics.FillRectangle(fill, x + Scale(2), Scale(23) - fillHeight, Scale(6), fillHeight);
        }
        using var centered = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None };
        graphics.DrawString(label, normalFont, mutedBrush, new RectangleF(x + Scale(15), y, Scale(12), Scale(24)), centered);
        graphics.DrawString(percent > 0 ? $"{percent}%" : "—", boldFont, textBrush, new RectangleF(x + Scale(27), y, Scale(30), Scale(24)), centered);
        if (charging)
        {
            using var bolt = new SolidBrush(_palette.Charging);
            var points = new[] { new Point(x + Scale(61), Scale(7)), new Point(x + Scale(57), Scale(16)), new Point(x + Scale(61), Scale(16)), new Point(x + Scale(57), Scale(25)) };
            graphics.FillPolygon(bolt, points);
        }
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath(); var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90); path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }

    private int Scale(int value) => Math.Max(1, (int)Math.Round(value * _dpi / 96d));
    private static int ClampBattery(double value) => value <= 0 ? 0 : (int)Math.Round(Math.Clamp(value, 0, 100));

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var self = GetWindowLongPtr(hwnd, GwlUserdata);
        var window = self == IntPtr.Zero ? null : GCHandle.FromIntPtr(self).Target as NativeTaskbarBatteryWindow;
        if (message == WmNccreate && lParam != IntPtr.Zero)
        {
            var create = Marshal.PtrToStructure<CreateStruct>(lParam);
            SetWindowLongPtr(hwnd, GwlUserdata, create.CreateParams);
            window = GCHandle.FromIntPtr(create.CreateParams).Target as NativeTaskbarBatteryWindow;
        }
        if (window is null) return DefWindowProc(hwnd, message, wParam, lParam);
        switch (message)
        {
            case WmPaint:
                var paintHdc = BeginPaint(hwnd, out var paint); if (paintHdc != IntPtr.Zero) { window.Paint(paintHdc); EndPaint(hwnd, ref paint); } return IntPtr.Zero;
            case WmEraseBkgnd: return new IntPtr(1);
            case WmLbuttonup: window._showMainWindow(); return IntPtr.Zero;
            case WmMouseactivate: return new IntPtr(MaNoactivate);
            case WmTimer: window.PositionOnTaskbar(false); return IntPtr.Zero;
            case WmDpiChanged: window.EnsureDpi(); window.PositionOnTaskbar(true); return IntPtr.Zero;
            case WmDisplaychange: case WmSettingchange: window.EnsureCreated(); window.PositionOnTaskbar(true); window.Invalidate(); return IntPtr.Zero;
            default:
                if (message == TaskbarCreatedMessage) { window.EnsureCreated(); window.PositionOnTaskbar(true); window.Invalidate(); return IntPtr.Zero; }
                if (message == WmNcdestroy) { window._hwnd = IntPtr.Zero; return IntPtr.Zero; }
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void Invalidate() { if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false); }
    private void DestroyNativeWindow()
    {
        if (_hwnd != IntPtr.Zero) { KillTimer(_hwnd, TimerId); DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _hostWatchTimer.Stop(); DestroyNativeWindow(); _taskbarHwnd = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct WndClassEx { public uint Size, Style; public WndProcDelegate WindowProc; public int ClsExtra, WndExtra; public IntPtr Instance, Icon, Cursor, Background; [MarshalAs(UnmanagedType.LPWStr)] public string MenuName; [MarshalAs(UnmanagedType.LPWStr)] public string ClassName; public IntPtr SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct CreateStruct { public IntPtr CreateParams; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct { public IntPtr Hdc; public int Erase; public NativeRect Paint; public int Restore, IncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WndClassEx windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursor);
    [DllImport("user32.dll")] private static extern uint SetTimer(IntPtr hwnd, uint id, uint interval, IntPtr callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, uint id);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern uint RegisterWindowMessage(string message);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90); path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90); path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
