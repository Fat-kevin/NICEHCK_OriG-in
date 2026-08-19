using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

/// <summary>管理任务栏进度和可关闭的原生状态胶囊；不申请工作区、不抢焦点。</summary>
public sealed class TaskbarOverlayService : IDisposable
{
    private readonly MainWindow _window;
    private readonly DashboardViewModel _viewModel;
    private readonly DesktopPreferencesService _preferences;
    private readonly DesktopThemeService _theme;
    private readonly ILogger<TaskbarOverlayService> _logger;
    private IntPtr _hwnd;
    private ITaskbarList3? _taskbar;
    private NativeTaskbarBatteryWindow? _batteryWindow;
    private int _disposed;
    private int _refreshQueued;
    private int _paletteRefreshRequested;
    private bool _batteryWindowShown;

    public TaskbarOverlayService(MainWindow window, DashboardViewModel viewModel, DesktopPreferencesService preferences, DesktopThemeService theme, ILogger<TaskbarOverlayService> logger)
    {
        _window = window; _viewModel = viewModel; _preferences = preferences; _theme = theme; _logger = logger;
        _window.SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _preferences.PreferencesChanged += OnPreferencesChanged;
        _theme.ThemeChanged += OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return;
        try { _taskbar = (ITaskbarList3)(object)new CTaskbarList(); _taskbar.HrInit(); }
        catch (Exception ex) { DisableTaskbarOverlay(ex); }
        ApplyRefresh(true);
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (e.PropertyName is not (nameof(DashboardViewModel.IsConnected) or nameof(DashboardViewModel.IsSearching)
            or nameof(DashboardViewModel.LeftBatteryValue) or nameof(DashboardViewModel.RightBatteryValue)
            or nameof(DashboardViewModel.CaseBatteryValue) or nameof(DashboardViewModel.LeftChargeText)
            or nameof(DashboardViewModel.RightChargeText) or nameof(DashboardViewModel.TaskbarWidgetEnabled)
            or nameof(DashboardViewModel.BatteryAccentColor) or nameof(DashboardViewModel.ChargingColor))) return;
        QueueRefresh(false);
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        QueueRefresh(true);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => QueueRefresh(true);

    private void QueueRefresh(bool updatePalette)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (updatePalette) Interlocked.Exchange(ref _paletteRefreshRequested, 1);
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;

        try
        {
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (Volatile.Read(ref _disposed) != 0) return;
                var refreshPalette = Interlocked.Exchange(ref _paletteRefreshRequested, 0) != 0;
                ApplyRefresh(refreshPalette);
            }));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            _logger.LogDebug(ex, "任务栏刷新已跳过：窗口调度器正在关闭");
        }
    }

    private void ApplyRefresh(bool updatePalette)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            EnsureBatteryWindow(updatePalette);
            UpdateBatteryWindow();
            UpdateOverlay();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "任务栏状态更新失败，主界面继续运行");
        }
    }

    private void EnsureBatteryWindow(bool updatePalette)
    {
        if (!_viewModel.TaskbarWidgetEnabled)
        {
            _batteryWindow?.Dispose();
            _batteryWindow = null;
            _batteryWindowShown = false;
            ClearTaskbarProgress();
            return;
        }
        if (_batteryWindow is null)
        {
            try
            {
                _batteryWindow = new NativeTaskbarBatteryWindow(ShowMainWindow, _window.Dispatcher, BuildPalette());
                _batteryWindowShown = false;
            }
            catch (Exception ex) { _logger.LogError(ex, "原生任务栏状态胶囊初始化失败"); _batteryWindow = null; }
        }
        else if (updatePalette)
        {
            _batteryWindow.UpdatePalette(BuildPalette());
        }
    }

    private void UpdateBatteryWindow()
    {
        if (_batteryWindow is null) return;
        var leftCharging = !string.IsNullOrEmpty(_viewModel.LeftChargeText);
        var rightCharging = !string.IsNullOrEmpty(_viewModel.RightChargeText);
        _batteryWindow.UpdateStatus(_viewModel.IsConnected, _viewModel.IsSearching, _viewModel.LeftBatteryValue, _viewModel.RightBatteryValue, leftCharging, rightCharging);
        if (!_batteryWindowShown)
        {
            _batteryWindowShown = _batteryWindow.Show();
        }
    }

    private NativeTaskbarPalette BuildPalette()
    {
        var preferences = _preferences.Current;
        var accent = BatteryColorResolver.Parse(preferences.BatteryAccentColor, BatteryColorResolver.LowColor);
        var charging = BatteryColorResolver.Parse(preferences.ChargingColor, BatteryColorResolver.LowColor);
        return new NativeTaskbarPalette(ToDrawing(accent), ToDrawing(charging), _theme.IsDark);
    }

    private static System.Drawing.Color ToDrawing(System.Windows.Media.Color color) => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    private void UpdateOverlay()
    {
        if (_hwnd == IntPtr.Zero || _taskbar is null) return;
        try
        {
            if (!_viewModel.TaskbarWidgetEnabled) { ClearTaskbarProgress(); return; }
            var percent = _viewModel.IsConnected ? ResolveBatteryPercent() : 0;
            if (percent <= 0) { _taskbar.SetProgressState(_hwnd, TbpFlag.NoProgress); return; }
            _taskbar.SetProgressState(_hwnd, TbpFlag.Normal); _taskbar.SetProgressValue(_hwnd, (ulong)percent, 100);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "任务栏电量进度更新失败"); DisableTaskbarOverlay(ex); }
    }

    private void ClearTaskbarProgress()
    {
        if (_hwnd == IntPtr.Zero || _taskbar is null) return;
        try { _taskbar.SetProgressState(_hwnd, TbpFlag.NoProgress); } catch (COMException) { }
    }

    private void DisableTaskbarOverlay(Exception exception)
    {
        _logger.LogInformation(exception, "任务栏 COM 不可用，继续运行原生状态胶囊");
        if (_taskbar is not null && Marshal.IsComObject(_taskbar))
        {
            try { Marshal.FinalReleaseComObject(_taskbar); } catch (Exception ex) { _logger.LogDebug(ex, "释放任务栏 COM 失败"); }
        }
        _taskbar = null;
    }

    private void ShowMainWindow()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _window.Dispatcher.InvokeAsync(() => { _window.Show(); if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal; _window.Activate(); });
    }

    private double ResolveBatteryPercent() => _viewModel.LeftBatteryValue > 0 ? _viewModel.LeftBatteryValue : _viewModel.RightBatteryValue > 0 ? _viewModel.RightBatteryValue : _viewModel.CaseBatteryValue;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ClearTaskbarProgress(); _viewModel.PropertyChanged -= OnViewModelChanged; _preferences.PreferencesChanged -= OnPreferencesChanged; _theme.ThemeChanged -= OnThemeChanged; _window.SourceInitialized -= OnSourceInitialized;
        _batteryWindow?.Dispose(); _batteryWindow = null; if (_taskbar is not null) DisableTaskbarOverlay(new ObjectDisposedException(nameof(TaskbarOverlayService)));
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ITaskbarList { void HrInit(); void AddTab(IntPtr hwnd); void DeleteTab(IntPtr hwnd); void ActivateTab(IntPtr hwnd); void SetActiveAlt(IntPtr hwnd); }
    [ComImport, Guid("602D4995-B13A-429B-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ITaskbarList2 : ITaskbarList { void MarkFullscreenWindow(IntPtr hwnd, bool fullscreen); }
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ITaskbarList3 : ITaskbarList2
    {
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total); void SetProgressState(IntPtr hwnd, TbpFlag flags); void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi); void UnregisterTab(IntPtr hwndTab); void SetTabOrder(IntPtr hwndTab, IntPtr insertBefore); void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi); void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons); void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons); void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList); void SetOverlayIcon(IntPtr hwnd, IntPtr icon, string description); void SetThumbnailTooltip(IntPtr hwnd, string tooltip); void SetThumbnailClip(IntPtr hwnd, ref RECT rect);
    }
    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)] private sealed class CTaskbarList { }
    private enum TbpFlag : uint { NoProgress = 0, Normal = 0x2 }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
}
