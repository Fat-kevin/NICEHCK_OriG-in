using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

/// <summary>连接成功时显示右下角设备通知；通知内容直接绑定主视图模型，避免出现两套状态。</summary>
public sealed class ConnectionToastService : IDisposable
{
    private readonly DashboardViewModel _viewModel;
    private readonly MainWindow _mainWindow;
    private readonly DesktopThemeService _theme;
    private readonly ILogger<ConnectionToastService> _logger;
    private ConnectionToastWindow? _window;
    private CancellationTokenSource? _dismissCts;
    private CancellationTokenSource? _transitionCts;
    private bool _wasConnected;
    private bool _disposed;

    public ConnectionToastService(
        DashboardViewModel viewModel,
        MainWindow mainWindow,
        DesktopThemeService theme,
        ILogger<ConnectionToastService> logger)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _theme = theme;
        _logger = logger;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _wasConnected = viewModel.IsConnected;
        if (_wasConnected)
        {
            ShowToast();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
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
        catch (Exception ex)
        {
            // 连接提醒是可选 UI，不应因动画或窗口状态异常影响主界面。
            _logger.LogError(ex, "更新连接提醒失败");
        }
    }

    private void ShowToast()
    {
        if (_disposed)
        {
            return;
        }

        if (!_mainWindow.Dispatcher.CheckAccess())
        {
            PostToUi(ShowToast);
            return;
        }

        CancelTransition();
        _window ??= new ConnectionToastWindow(_viewModel, ShowMainWindow, _theme);
        _window.ReplayConnectionAnimation();
        _dismissCts?.Cancel();
        var dismiss = new CancellationTokenSource();
        _dismissCts = dismiss;
        _ = DismissLaterAsync(dismiss);
    }

    private async Task DismissLaterAsync(CancellationTokenSource dismiss)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), dismiss.Token);
            if (!dismiss.IsCancellationRequested && !_mainWindow.Dispatcher.HasShutdownStarted)
            {
                await _mainWindow.Dispatcher.InvokeAsync(HideToast);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "关闭连接提醒的延迟任务结束");
        }
        finally
        {
            if (ReferenceEquals(_dismissCts, dismiss))
            {
                _dismissCts = null;
            }

            dismiss.Dispose();
        }
    }

    private void HideToast()
    {
        if (_disposed || _window is null)
        {
            return;
        }

        CancelTransition();
        _window.BeginAnimation(Window.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn },
        });
        var transition = new CancellationTokenSource();
        _transitionCts = transition;
        _ = CompleteHideAsync(transition);
    }

    private async Task CompleteHideAsync(CancellationTokenSource transition)
    {
        try
        {
            await Task.Delay(240, transition.Token);
            if (!transition.IsCancellationRequested && !_disposed && !_mainWindow.Dispatcher.HasShutdownStarted)
            {
                await _mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    if (_window is { IsVisible: true })
                    {
                        _window.Hide();
                    }

                    if (_window is not null)
                    {
                        _window.Opacity = 1;
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "关闭连接提醒动画失败");
        }
        finally
        {
            if (ReferenceEquals(_transitionCts, transition))
            {
                _transitionCts = null;
            }

            transition.Dispose();
        }
    }

    private void ShowMainWindow()
    {
        if (_disposed)
        {
            return;
        }

        if (!_mainWindow.Dispatcher.CheckAccess())
        {
            PostToUi(ShowMainWindow);
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

    private void PostToUi(Action action)
    {
        try
        {
            if (_mainWindow.Dispatcher.HasShutdownStarted)
            {
                return;
            }

            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { _logger.LogError(ex, "执行连接提醒 UI 操作失败"); }
            }));
        }
        catch (InvalidOperationException)
        {
            // Dispatcher 正在关闭，忽略迟到的 UI 更新。
        }
    }

    private void CancelTransition()
    {
        var transition = _transitionCts;
        _transitionCts = null;
        if (transition is null)
        {
            return;
        }

        transition.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _dismissCts?.Cancel();
        _dismissCts = null;
        CancelTransition();
        _window?.Close();
        _window = null;
    }
}
