using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 使用 Windows 官方任务栏扩展显示耳机状态：任务栏按钮覆盖图标 + 单耳优先电量进度。
/// 精确左右耳百分比由通知区域图标的提示文本提供，不创建独立悬浮窗口。
/// </summary>
public sealed class TaskbarOverlayService : IDisposable
{
    private readonly MainWindow _window;
    private readonly DashboardViewModel _viewModel;
    private IntPtr _hwnd;
    private ITaskbarList3? _taskbar;
    private HwndSource? _source;
    private IntPtr _overlayIcon;
    private static readonly uint TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");

    public TaskbarOverlayService(MainWindow window, DashboardViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _window.SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WindowProc);

        try
        {
            _taskbar = (ITaskbarList3)new CTaskbarList();
            _taskbar.HrInit();
        }
        catch (COMException)
        {
            // 远程会话或精简环境没有任务栏 COM 对象时，主界面仍可正常运行。
            _taskbar = null;
        }

        UpdateOverlay();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.IsConnected)
            or nameof(DashboardViewModel.IsSearching)
            or nameof(DashboardViewModel.LeftBatteryText)
            or nameof(DashboardViewModel.RightBatteryText)
            or nameof(DashboardViewModel.CaseBatteryText)
            or nameof(DashboardViewModel.LeftChargeText)
            or nameof(DashboardViewModel.RightChargeText)
            or nameof(DashboardViewModel.LeftBatteryValue)
            or nameof(DashboardViewModel.RightBatteryValue)
            or nameof(DashboardViewModel.CaseBatteryValue))
        {
            UpdateOverlay();
        }
    }

    private void UpdateOverlay()
    {
        if (_hwnd == IntPtr.Zero || _taskbar is null)
        {
            return;
        }

        UpdateTaskbarIcon();

        // 任务栏进度条作为单耳优先的紧凑状态提示；详细左右耳百分比在通知区域提示中显示。
        var percent = _viewModel.IsConnected ? ResolveBatteryPercent() : 0;
        if (percent <= 0)
        {
            _taskbar.SetProgressState(_hwnd, TbpFlag.NoProgress);
            return;
        }

        _taskbar.SetProgressState(_hwnd, TbpFlag.Normal);
        _taskbar.SetProgressValue(_hwnd, (ulong)percent, 100);
    }

    private void UpdateTaskbarIcon()
    {
        var nextIcon = BatteryStatusIconFactory.Create(
            _viewModel.IsConnected,
            _viewModel.LeftBatteryValue,
            _viewModel.RightBatteryValue,
            !string.IsNullOrEmpty(_viewModel.LeftChargeText),
            !string.IsNullOrEmpty(_viewModel.RightChargeText));

        try
        {
            _taskbar!.SetOverlayIcon(_hwnd, nextIcon, BuildStatusDescription());
            BatteryStatusIconFactory.Destroy(_overlayIcon);
            _overlayIcon = nextIcon;
        }
        catch (COMException)
        {
            BatteryStatusIconFactory.Destroy(nextIcon);
        }
    }

    private string BuildStatusDescription()
    {
        if (!_viewModel.IsConnected)
        {
            return _viewModel.IsSearching ? "原点耳机：正在连接" : "原点耳机：等待连接";
        }

        var left = FormatBattery(_viewModel.LeftBatteryValue);
        var right = FormatBattery(_viewModel.RightBatteryValue);
        return $"原点耳机：左耳 {left} · 右耳 {right}";
    }

    private static string FormatBattery(double value) => value > 0 ? $"{Math.Round(value):0}%" : "—";

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == TaskbarButtonCreatedMessage)
        {
            UpdateOverlay();
        }

        return IntPtr.Zero;
    }

    /// <summary>左耳优先，其次右耳，再充电盒；未知（0）时返回 0。</summary>
    private double ResolveBatteryPercent()
    {
        if (_viewModel.LeftBatteryValue > 0)
        {
            return _viewModel.LeftBatteryValue;
        }

        if (_viewModel.RightBatteryValue > 0)
        {
            return _viewModel.RightBatteryValue;
        }

        return _viewModel.CaseBatteryValue > 0 ? _viewModel.CaseBatteryValue : 0;
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero && _taskbar is not null)
        {
            try
            {
                _taskbar.SetProgressState(_hwnd, TbpFlag.NoProgress);
                _taskbar.SetOverlayIcon(_hwnd, IntPtr.Zero, string.Empty);
            }
            catch (COMException)
            {
                // 任务栏已退出时忽略清理失败。
            }
        }

        _viewModel.PropertyChanged -= OnViewModelChanged;
        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WindowProc);
        _source = null;
        BatteryStatusIconFactory.Destroy(_overlayIcon);
        _overlayIcon = IntPtr.Zero;
        _taskbar = null;
    }

    // ---- ITaskbarList3 COM 定义（经典已知结构，Vtable 顺序必须保持） ----

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport, Guid("602D4995-B13A-429B-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2 : ITaskbarList
    {
        void MarkFullscreenWindow(IntPtr hwnd, bool fullscreen);
    }

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3 : ITaskbarList2
    {
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TbpFlag tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, string pszTip);
        void SetThumbnailClip(IntPtr hwnd, ref RECT prcClip);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList
    {
    }

    private enum TbpFlag : uint
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
