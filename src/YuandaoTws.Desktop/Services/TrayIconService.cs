using System.Runtime.InteropServices;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using YuandaoTws.Application.Services;
using YuandaoTws.Desktop.ViewModels;
using YuandaoTws.Domain.Enums;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 使用 Shell_NotifyIcon 原生维护通知区域状态图标，不创建常驻悬浮窗口。
/// 左右耳电量通过动态图标直观显示，精确百分比通过提示文本显示。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint TrayIconId = 1;
    private const int WmApp = 0x8000;
    private const int WmTrayCallback = WmApp + 0x52;
    private const uint WmLbuttonup = 0x0202;
    private const uint WmLbuttondblclk = 0x0203;
    private const uint WmRbuttonup = 0x0205;
    private const uint WmContextmenu = 0x007B;
    private const uint NinSelect = 0x0400;
    private const uint NinKeyselect = 0x0401;

    private readonly DashboardViewModel _viewModel;
    private readonly MainWindow _mainWindow;
    private readonly NoiseCancellingService _anc;
    private readonly ContextMenu _contextMenu = new();
    private readonly Dictionary<NoiseCancellingMode, MenuItem> _ancItems = new();
    private readonly IDisposable _ancSubscription;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private IntPtr _iconHandle;
    private bool _iconAdded;
    private bool _disposed;
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    public TrayIconService(DashboardViewModel viewModel, MainWindow mainWindow, NoiseCancellingService anc)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _anc = anc;
        AddAncSubmenu();
        AddMenuItem("打开控制面板", (_, _) => ShowMainWindow());
        AddMenuItem("重新连接耳机", async (_, _) => await _viewModel.ForceReconnectAsync());
        AddMenuItem("退出", (_, _) => System.Windows.Application.Current.Shutdown());

        _mainWindow.SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _ancSubscription = anc.ModeChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(UpdateAncCheckmarks);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WindowProc);
        UpdateTrayIcon();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.IsConnected)
            or nameof(DashboardViewModel.IsSearching)
            or nameof(DashboardViewModel.LeftBatteryValue)
            or nameof(DashboardViewModel.RightBatteryValue)
            or nameof(DashboardViewModel.CaseBatteryValue)
            or nameof(DashboardViewModel.LeftChargeText)
            or nameof(DashboardViewModel.RightChargeText))
        {
            UpdateTrayIcon();
        }
    }

    private void UpdateTrayIcon()
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var nextIcon = BatteryStatusIconFactory.Create(
            _viewModel.IsConnected,
            _viewModel.LeftBatteryValue,
            _viewModel.RightBatteryValue,
            !string.IsNullOrEmpty(_viewModel.LeftChargeText),
            !string.IsNullOrEmpty(_viewModel.RightChargeText));
        var data = CreateNotifyIconData(nextIcon);
        var success = _iconAdded
            ? Shell_NotifyIcon(NimModify, ref data)
            : Shell_NotifyIcon(NimAdd, ref data);
        if (!success && _iconAdded)
        {
            // Explorer 重启后原图标句柄仍在，但通知区域项目已经被 Shell 清除。
            _iconAdded = false;
            success = Shell_NotifyIcon(NimAdd, ref data);
        }

        if (!success)
        {
            BatteryStatusIconFactory.Destroy(nextIcon);
            return;
        }

        if (!_iconAdded)
        {
            var versionData = CreateNotifyIconData(nextIcon);
            versionData.VersionOrTimeout = NotifyIconVersion4;
            _ = Shell_NotifyIcon(NimSetVersion, ref versionData);
            _iconAdded = true;
        }

        BatteryStatusIconFactory.Destroy(_iconHandle);
        _iconHandle = nextIcon;
    }

    private NotifyIconData CreateNotifyIconData(IntPtr icon)
    {
        return new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _hwnd,
            Id = TrayIconId,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = WmTrayCallback,
            Icon = icon,
            Tip = BuildTooltip(),
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
    }

    private string BuildTooltip()
    {
        if (!_viewModel.IsConnected)
        {
            return _viewModel.IsSearching ? "原点耳机 · 正在连接" : "原点耳机 · 等待连接";
        }

        var left = FormatBattery(_viewModel.LeftBatteryValue);
        var right = FormatBattery(_viewModel.RightBatteryValue);
        var charging = !string.IsNullOrEmpty(_viewModel.LeftChargeText)
            || !string.IsNullOrEmpty(_viewModel.RightChargeText)
            ? " · 充电中"
            : string.Empty;
        return $"原点耳机 · 左耳 {left} · 右耳 {right}{charging}";
    }

    private static string FormatBattery(double value) => value > 0 ? $"{Math.Round(value):0}%" : "—";

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == TaskbarCreatedMessage)
        {
            _iconAdded = false;
            UpdateTrayIcon();
            return IntPtr.Zero;
        }

        if (message != WmTrayCallback)
        {
            return IntPtr.Zero;
        }

        var trayMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
        switch (trayMessage)
        {
            case WmLbuttonup:
            case WmLbuttondblclk:
            case NinSelect:
            case NinKeyselect:
                ShowMainWindow();
                break;
            case WmRbuttonup:
            case WmContextmenu:
                ShowContextMenu();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        _contextMenu.PlacementTarget = _mainWindow;
        _contextMenu.Placement = PlacementMode.AbsolutePoint;
        _contextMenu.HorizontalOffset = point.X;
        _contextMenu.VerticalOffset = point.Y;
        _contextMenu.IsOpen = true;
    }

    private void AddAncSubmenu()
    {
        var ancMenu = new MenuItem { Header = "降噪模式" };
        foreach (var (mode, label) in AncModes())
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
            };
            var selected = mode;
            item.Click += async (_, _) => await SetAncFromTrayAsync(selected);
            _ancItems[mode] = item;
            ancMenu.Items.Add(item);
        }

        _contextMenu.Items.Add(ancMenu);
    }

    private async Task SetAncFromTrayAsync(NoiseCancellingMode mode)
    {
        try
        {
            await _anc.SetModeAsync(mode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "降噪切换失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateAncCheckmarks(NoiseCancellingMode mode)
    {
        foreach (var (candidate, item) in _ancItems)
        {
            item.IsChecked = candidate == mode;
        }
    }

    private static (NoiseCancellingMode Mode, string Label)[] AncModes() => new[]
    {
        (NoiseCancellingMode.Off, "关闭"),
        (NoiseCancellingMode.Transparency, "通透"),
        (NoiseCancellingMode.Normal, "普通"),
        (NoiseCancellingMode.Deep, "深度"),
        (NoiseCancellingMode.Experimental, "试验"),
        (NoiseCancellingMode.WindSuppression, "风噪"),
    };

    private void AddMenuItem(string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text };
        item.Click += handler;
        _contextMenu.Items.Add(item);
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ancSubscription.Dispose();
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _mainWindow.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WindowProc);
        _source = null;

        if (_iconAdded && _hwnd != IntPtr.Zero)
        {
            var data = CreateNotifyIconData(_iconHandle);
            _ = Shell_NotifyIcon(NimDelete, ref data);
        }

        BatteryStatusIconFactory.Destroy(_iconHandle);
        _iconHandle = IntPtr.Zero;
        _iconAdded = false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint VersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
