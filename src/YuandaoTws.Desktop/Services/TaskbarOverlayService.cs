using System;
using System.Windows;
using System.Windows.Interop;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

public sealed class TaskbarOverlayService : IDisposable
{
    private readonly Window _window;
    private readonly DashboardViewModel _viewModel;
    private HwndSource? _source;

    public TaskbarOverlayService(Window window, DashboardViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _window.SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(_window)!;
        UpdateOverlay();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.LeftBatteryText) or nameof(DashboardViewModel.RightBatteryText) or nameof(DashboardViewModel.CaseBatteryText) or nameof(DashboardViewModel.StatusText)) UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        // Windows 任务栏覆盖图标需要真实 PNG/ICO 资源和 ITaskbarList3。
        // v1 先保留明确的桥接点，避免把 UI 状态与 Win32 句柄耦合在 ViewModel 中。
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _window.SourceInitialized -= OnSourceInitialized;
        _source = null;
    }
}
