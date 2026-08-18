using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 在任务栏图标上叠加耳机电池进度（ITaskbarList3.SetProgressValue / SetProgressState）。
/// 电池来源取 ViewModel 中左耳优先、右耳次之、充电盒兜底；未连接时清除进度条。
/// </summary>
public sealed class TaskbarOverlayService : IDisposable
{
    private readonly MainWindow _window;
    private readonly DashboardViewModel _viewModel;
    private readonly ILogger<TaskbarOverlayService> _logger;
    private IntPtr _hwnd;
    private ITaskbarList3? _taskbar;
    private NativeTaskbarBatteryWindow? _batteryWindow;

    public TaskbarOverlayService(MainWindow window, DashboardViewModel viewModel, ILogger<TaskbarOverlayService> logger)
    {
        _window = window;
        _viewModel = viewModel;
        _logger = logger;
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

        try
        {
            _taskbar = (ITaskbarList3)new CTaskbarList();
        }
        catch (COMException)
        {
            // 部分远程会话 / 精简环境没有任务栏 COM 对象，静默降级为无任务栏进度。
            _taskbar = null;
        }

        UpdateOverlay();
        try
        {
            _batteryWindow = new NativeTaskbarBatteryWindow(ShowMainWindow, _window.Dispatcher);
            _batteryWindow.Show(
                _viewModel.IsConnected,
                _viewModel.IsSearching,
                _viewModel.LeftBatteryValue,
                _viewModel.RightBatteryValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "原生任务栏电量控件初始化失败，将继续运行主界面和任务栏进度");
            _batteryWindow?.Dispose();
            _batteryWindow = null;
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.IsConnected)
            or nameof(DashboardViewModel.IsSearching)
            or nameof(DashboardViewModel.LeftBatteryText)
            or nameof(DashboardViewModel.RightBatteryText)
            or nameof(DashboardViewModel.CaseBatteryText)
            or nameof(DashboardViewModel.LeftBatteryValue)
            or nameof(DashboardViewModel.RightBatteryValue)
            or nameof(DashboardViewModel.CaseBatteryValue))
        {
            _batteryWindow?.UpdateStatus(
                _viewModel.IsConnected,
                _viewModel.IsSearching,
                _viewModel.LeftBatteryValue,
                _viewModel.RightBatteryValue);
            UpdateOverlay();
        }
    }

    private void UpdateOverlay()
    {
        if (_hwnd == IntPtr.Zero || _taskbar is null)
        {
            return;
        }

        // 只读 VM 暴露的绑定源，不引入新状态：连接成功后才显示进度，断开/未知显示 0。
        var percent = _viewModel.IsConnected ? ResolveBatteryPercent() : 0;
        if (percent <= 0)
        {
            _taskbar.SetProgressState(_hwnd, TbpFlag.NoProgress);
            return;
        }

        _taskbar.SetProgressState(_hwnd, TbpFlag.Normal);
        _taskbar.SetProgressValue(_hwnd, (ulong)percent, 100);
    }

    private void ShowMainWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
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
            }
            catch (COMException)
            {
                // 窗口已销毁时忽略，不影响退出。
            }
        }

        _viewModel.PropertyChanged -= OnViewModelChanged;
        _window.SourceInitialized -= OnSourceInitialized;
        _batteryWindow?.Dispose();
        _batteryWindow = null;
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
}
