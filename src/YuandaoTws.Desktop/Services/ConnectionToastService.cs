using System.ComponentModel;
using System.Windows;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

/// <summary>连接成功时显示右下角设备通知；通知内容直接绑定主视图模型，避免出现两套状态。</summary>
public sealed class ConnectionToastService : IDisposable
{
    private readonly DashboardViewModel _viewModel;
    private readonly MainWindow _mainWindow;
    private ConnectionToastWindow? _window;
    private CancellationTokenSource? _dismissCts;
    private bool _wasConnected;

    public ConnectionToastService(DashboardViewModel viewModel, MainWindow mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _wasConnected = viewModel.IsConnected;
        if (_wasConnected)
        {
            ShowToast();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.IsConnected))
        {
            if (_viewModel.IsConnected && !_wasConnected)
            {
                _wasConnected = true;
                ShowToast();
            }
            else if (!_viewModel.IsConnected)
            {
                _wasConnected = false;
                HideToast();
            }
        }

        if (e.PropertyName == nameof(DashboardViewModel.CasePresent))
        {
            _window?.UpdateCaseLayout(_viewModel.CasePresent);
        }
    }

    private void ShowToast()
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(ShowToast);
            return;
        }

        _window ??= new ConnectionToastWindow(_viewModel, ShowMainWindow);
        _window.ReplayConnectionAnimation();
        _dismissCts?.Cancel();
        _dismissCts?.Dispose();
        _dismissCts = new CancellationTokenSource();
        _ = DismissLaterAsync(_dismissCts.Token);
    }

    private async Task DismissLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), token);
            if (!token.IsCancellationRequested)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(HideToast);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HideToast()
    {
        if (_window is null) return;
        _window.BeginAnimation(Window.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
        });
        _ = Task.Delay(240).ContinueWith(_ => System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is { IsVisible: true }) _window.Hide();
            if (_window is not null) _window.Opacity = 1;
        }));
    }

    private void ShowMainWindow()
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(ShowMainWindow);
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        HideToast();
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _dismissCts?.Cancel();
        _dismissCts?.Dispose();
        _window?.Close();
        _window = null;
    }
}
