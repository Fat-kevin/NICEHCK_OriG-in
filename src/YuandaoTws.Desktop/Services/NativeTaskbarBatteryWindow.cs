using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 使用一个轻量级 Win32 工具窗口绘制左下角电量摘要。
///
/// Windows 没有公开 API 允许普通应用把自定义文字控件嵌入 Explorer 任务栏，
/// 因此这里使用不抢焦点的原生 HWND，直接贴近任务栏定位，不申请桌面工作区，
/// 避免影响其他窗口的大小和位置。绘制仍然是原生 GDI+，不是浏览器或 WPF 透明窗。
/// </summary>
public sealed class NativeTaskbarBatteryWindow : IDisposable
{
    private const string WindowClassName = "YuandaoTws.NativeTaskbarBatteryWindow";
    private const int BaseWidth = 198;
    private const int BaseHeight = 36;
    private const int BaseRadius = 10;
    private const int HorizontalOffset = 240;
    private const uint TimerId = 1;
    private const uint TimerIntervalMs = 2500;

    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoSendChanging = 0x0400;
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
    private const uint MonitorDefaultToPrimary = 1;

    private const int HtClient = 1;
    private const int MaNoactivate = 3;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int GwlUserdata = -21;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly WndProcDelegate WindowProcDelegate = WindowProc;
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private static ushort _windowClassAtom;

    private readonly Action _showMainWindow;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _hostWatchTimer;
    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private GCHandle _selfHandle;
    private int _dpi = 96;
    private int _width = BaseWidth;
    private int _height = BaseHeight;
    private bool _isConnected;
    private bool _isSearching;
    private int _leftBattery;
    private int _rightBattery;
    private bool _disposed;

    public NativeTaskbarBatteryWindow(Action showMainWindow, Dispatcher dispatcher)
    {
        _showMainWindow = showMainWindow;
        _dispatcher = dispatcher;
        _hostWatchTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(TimerIntervalMs),
            DispatcherPriority.Background,
            (_, _) => MaintainTaskbarHost(),
            dispatcher);
    }

    public void Show(bool isConnected, bool isSearching, double leftBattery, double rightBattery)
    {
        if (_disposed)
        {
            return;
        }

        UpdateStatus(isConnected, isSearching, leftBattery, rightBattery);
        _hostWatchTimer.Start();
        EnsureCreated();
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(_hwnd, SwShownoactivate);
        EnsureDpi();
        PositionOnTaskbar(force: true);
    }

    public void UpdateStatus(bool isConnected, bool isSearching, double leftBattery, double rightBattery)
    {
        _isConnected = isConnected;
        _isSearching = isSearching;
        _leftBattery = isConnected ? ClampBattery(leftBattery) : 0;
        _rightBattery = isConnected ? ClampBattery(rightBattery) : 0;

        if (_hwnd != IntPtr.Zero)
        {
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void EnsureCreated()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (_hwnd != IntPtr.Zero && (_taskbarHwnd == taskbar || taskbar == IntPtr.Zero))
        {
            return;
        }

        DestroyNativeWindow();
        _taskbarHwnd = taskbar;
        RegisterWindowClass();
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = CreateWindowEx(
            WsExToolWindow | WsExNoActivate,
            WindowClassName,
            "原点耳机电量",
            WsPopup | WsVisible,
            0,
            0,
            _width,
            _height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            GCHandle.ToIntPtr(_selfHandle));

        if (_hwnd == IntPtr.Zero)
        {
            _selfHandle.Free();
            return;
        }

        ApplyNativeAppearance();
        SetTimer(_hwnd, TimerId, TimerIntervalMs, IntPtr.Zero);
    }

    private void MaintainTaskbarHost()
    {
        if (_disposed)
        {
            return;
        }

        EnsureCreated();
        if (_hwnd != IntPtr.Zero)
        {
            RestoreVisibility();
            PositionOnTaskbar(force: false);
        }
    }

    private void RestoreVisibility()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // 主窗口被点击、任务栏被激活后，Explorer 可能改变顶层窗口顺序。
        // 只恢复 Z 序和可见性，不改变尺寸和位置，避免产生闪烁。
        _ = SetWindowPos(
            _hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoActivate | SwpNoMove | SwpNoSize | SwpNoSendChanging | SwpShowWindow);
    }

    private void RegisterWindowClass()
    {
        if (_windowClassAtom != 0)
        {
            return;
        }

        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            Style = 0x0002, // CS_HREDRAW | CS_VREDRAW
            WindowProc = WindowProcDelegate,
            Instance = GetModuleHandle(null),
            Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)), // IDC_ARROW
            ClassName = WindowClassName,
        };

        _windowClassAtom = RegisterClassEx(ref windowClass);
        if (_windowClassAtom == 0 && Marshal.GetLastWin32Error() != 1410) // ERROR_CLASS_ALREADY_EXISTS
        {
            _windowClassAtom = 1;
        }
    }

    private void ApplyNativeAppearance()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var darkMode = 1;
        _ = DwmSetWindowAttribute(_hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        var cornerPreference = DwmCornerRound;
        _ = DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        ApplyWindowRegion();
    }

    private void EnsureDpi()
    {
        var dpi = _hwnd == IntPtr.Zero ? 96 : GetDpiForWindow(_hwnd);
        if (dpi == 0 || dpi == _dpi)
        {
            return;
        }

        _dpi = (int)dpi;
        _width = Scale(BaseWidth);
        _height = Scale(BaseHeight);
        ApplyWindowRegion();
    }

    private void PositionOnTaskbar(bool force)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        EnsureDpi();
        var workArea = GetWorkArea();
        var x = workArea.Left + Scale(HorizontalOffset);
        var y = workArea.Bottom - _height;
        if (_taskbarHwnd != IntPtr.Zero && GetWindowRect(_taskbarHwnd, out var taskbar))
        {
            var horizontal = taskbar.Width >= taskbar.Height * 2;
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
            }
        }

        if (!force && GetWindowRect(_hwnd, out var current)
            && current.Left == x && current.Top == y && current.Width == _width && current.Height == _height)
        {
            if (!IsWindowVisible(_hwnd))
            {
                _ = ShowWindow(_hwnd, SwShownoactivate);
            }
            return;
        }

        _ = SetWindowPos(_hwnd, HwndTopmost, x, y, _width, _height, SwpNoActivate | SwpNoSendChanging | SwpShowWindow);
    }

    private NativeRect GetWorkArea()
    {
        return new NativeRect
        {
            Left = 0,
            Top = 0,
            Right = GetSystemMetrics(SmCxScreen),
            Bottom = GetSystemMetrics(SmCyScreen),
        };
    }

    private void ApplyWindowRegion()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var radius = Scale(BaseRadius);
        var region = CreateRoundRectRgn(0, 0, _width + 1, _height + 1, radius * 2, radius * 2);
        _ = SetWindowRgn(_hwnd, region, true);
    }

    private void Paint(IntPtr hdc)
    {
        if (!GetClientRect(_hwnd, out var client))
        {
            return;
        }

        using var graphics = Graphics.FromHdc(hdc);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(255, 29, 36, 46));

        var bounds = new Rectangle(0, 0, client.Width - 1, client.Height - 1);
        using var borderPen = new Pen(Color.FromArgb(92, 255, 255, 255), Math.Max(1, Scale(1)));
        using var borderPath = RoundedPath(bounds, Scale(BaseRadius));
        graphics.DrawPath(borderPen, borderPath);

        using var dotBrush = new SolidBrush(_isConnected ? Color.FromArgb(52, 194, 107) : Color.FromArgb(145, 158, 170));
        var dotSize = Scale(6);
        graphics.FillEllipse(dotBrush, Scale(14), (client.Height - dotSize) / 2, dotSize, dotSize);

        using var labelFont = new Font("Segoe UI", Scale(8.5f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var valueFont = new Font("Segoe UI", Scale(11f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelBrush = new SolidBrush(Color.FromArgb(204, 221, 233));
        using var valueBrush = new SolidBrush(Color.White);
        using var cardBrush = new SolidBrush(Color.FromArgb(48, 255, 255, 255));
        using var cardPen = new Pen(Color.FromArgb(44, 255, 255, 255), Math.Max(1, Scale(1)));
        using var near = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

        if (!_isConnected)
        {
            var waitingText = _isSearching ? "正在连接…" : "等待连接";
            DrawText(graphics, waitingText, labelFont, labelBrush, new RectangleF(Scale(25), 0, client.Width - Scale(35), client.Height), near);
            return;
        }

        var statusRect = new RectangleF(Scale(25), 0, Scale(31), client.Height);
        DrawText(graphics, "耳机", labelFont, labelBrush, statusRect, near);

        DrawBatteryCard(graphics, new Rectangle(Scale(59), Scale(4), Scale(63), client.Height - Scale(8)), "左耳", _leftBattery, labelFont, valueFont, labelBrush, valueBrush, cardBrush, cardPen, near);
        DrawBatteryCard(graphics, new Rectangle(Scale(128), Scale(4), Scale(63), client.Height - Scale(8)), "右耳", _rightBattery, labelFont, valueFont, labelBrush, valueBrush, cardBrush, cardPen, near);
    }

    private void DrawBatteryCard(
        Graphics graphics,
        Rectangle bounds,
        string label,
        int value,
        Font labelFont,
        Font valueFont,
        Brush labelBrush,
        Brush valueBrush,
        Brush cardBrush,
        Pen cardPen,
        StringFormat center)
    {
        using var path = RoundedPath(bounds, Scale(6));
        graphics.FillPath(cardBrush, path);
        graphics.DrawPath(cardPen, path);
        DrawText(graphics, label, labelFont, labelBrush, new RectangleF(bounds.X, bounds.Y + Scale(1), bounds.Width, Scale(12)), center);
        DrawText(graphics, value > 0 ? $"{value}%" : "—", valueFont, valueBrush, new RectangleF(bounds.X, bounds.Y + Scale(12), bounds.Width, Scale(18)), center);
    }

    private static void DrawText(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds, StringFormat format)
    {
        graphics.DrawString(text, font, brush, bounds, format);
    }

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

    private int Scale(int value) => Math.Max(1, (int)Math.Round(value * _dpi / 96d));
    private float Scale(float value) => value * _dpi / 96f;
    private static int ClampBattery(double value)
    {
        return value is > 0 and <= 100 ? (int)Math.Round(value) : 0;
    }

    private IntPtr HandleMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == TaskbarCreatedMessage || message is WmDisplaychange or WmSettingchange)
        {
            EnsureCreated();
            PositionOnTaskbar(force: true);
            return IntPtr.Zero;
        }

        switch (message)
        {
            case WmPaint:
                var paint = new PaintStruct { Reserved = new byte[32] };
                var hdc = BeginPaint(_hwnd, ref paint);
                if (hdc != IntPtr.Zero)
                {
                    Paint(hdc);
                    EndPaint(_hwnd, ref paint);
                }
                return IntPtr.Zero;
            case WmEraseBkgnd:
                return new IntPtr(1);
            case WmTimer:
                PositionOnTaskbar(force: false);
                return IntPtr.Zero;
            case WmLbuttonup:
                _dispatcher.BeginInvoke(() =>
                {
                    _showMainWindow();
                    _dispatcher.BeginInvoke(
                        () =>
                        {
                            EnsureCreated();
                            RestoreVisibility();
                            PositionOnTaskbar(force: true);
                        },
                        DispatcherPriority.ApplicationIdle);
                }, DispatcherPriority.Normal);
                return IntPtr.Zero;
            case WmMouseactivate:
                return new IntPtr(MaNoactivate);
            case WmDpiChanged:
                EnsureDpi();
                ApplyWindowRegion();
                PositionOnTaskbar(force: true);
                return IntPtr.Zero;
            case WmNcdestroy:
                KillTimer(_hwnd, TimerId);
                _hwnd = IntPtr.Zero;
                _taskbarHwnd = IntPtr.Zero;
                return IntPtr.Zero;
            case WmNccreate:
                return new IntPtr(1);
            case 0x0084: // WM_NCHITTEST
                return new IntPtr(HtClient);
        }

        return DefWindowProc(_hwnd, message, wParam, lParam);
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmNccreate)
        {
            var create = Marshal.PtrToStructure<CreateStruct>(lParam);
            SetWindowLongPtr(hwnd, GwlUserdata, create.CreateParams);
        }

        var userData = GetWindowLongPtr(hwnd, GwlUserdata);
        if (userData != IntPtr.Zero)
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is NativeTaskbarBatteryWindow window)
            {
                return window.HandleMessage(message, wParam, lParam);
            }
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hostWatchTimer.Stop();
        DestroyNativeWindow();
    }

    private void DestroyNativeWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, TimerId);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _taskbarHwnd = IntPtr.Zero;
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateStruct
    {
        public IntPtr CreateParams;
        public IntPtr Instance;
        public IntPtr Menu;
        public IntPtr Parent;
        public int Height;
        public int Width;
        public int Top;
        public int Left;
        public IntPtr Style;
        public IntPtr Name;
        public IntPtr Class;
        public uint ExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public NativeRect Paint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[]? Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetTimer(IntPtr hwnd, uint timerId, uint interval, IntPtr callback);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hwnd, uint timerId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
