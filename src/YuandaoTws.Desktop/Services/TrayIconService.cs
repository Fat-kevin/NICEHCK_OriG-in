using System.Drawing;
using System.Reactive.Linq;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using YuandaoTws.Application.Services;
using YuandaoTws.Desktop.ViewModels;
using YuandaoTws.Domain.Enums;

namespace YuandaoTws.Desktop.Services;

/// <summary>系统托盘：打开/重连/退出 + 降噪模式快捷切换（勾选当前模式）。</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon = new();
    private readonly DashboardViewModel _viewModel;
    private readonly MainWindow _mainWindow;
    private readonly NoiseCancellingService _anc;
    private readonly Dictionary<NoiseCancellingMode, System.Windows.Controls.MenuItem> _ancItems = new();
    private readonly IDisposable _ancSubscription;

    public TrayIconService(DashboardViewModel viewModel, MainWindow mainWindow, NoiseCancellingService anc)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _anc = anc;
        _icon.Icon = ExtractAppIcon() ?? SystemIcons.Application;
        _icon.ToolTipText = "原点耳机控制";
        _icon.ContextMenu = new System.Windows.Controls.ContextMenu();
        AddAncSubmenu();
        AddMenuItem("打开控制面板", (_, _) => ShowMainWindow());
        AddMenuItem("重新连接耳机", async (_, _) => await _viewModel.ForceReconnectAsync());
        AddMenuItem("退出", (_, _) => System.Windows.Application.Current.Shutdown());
        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        // 跟随耳机状态更新降噪勾选（订阅发生在 UI 线程，ObserveOn 调度到 UI 线程回调）。
        _ancSubscription = anc.ModeChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(UpdateAncCheckmarks);
    }

    /// <summary>从当前 exe 提取应用图标；单文件发布下 <see cref="Environment.ProcessPath"/> 即实际 exe 路径。</summary>
    private static System.Drawing.Icon? ExtractAppIcon()
    {
        var processPath = Environment.ProcessPath;
        return string.IsNullOrEmpty(processPath) ? null : System.Drawing.Icon.ExtractAssociatedIcon(processPath);
    }

    /// <summary>在菜单顶部插入「降噪模式」子菜单，六态 ANC 一键切换。</summary>
    private void AddAncSubmenu()
    {
        var ancMenu = new System.Windows.Controls.MenuItem { Header = "降噪模式" };
        foreach (var (mode, label) in AncModes())
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = false,
            };
            var selected = mode;
            item.Click += async (_, _) => await SetAncFromTrayAsync(selected);
            _ancItems[mode] = item;
            ancMenu.Items.Add(item);
        }

        _icon.ContextMenu.Items.Add(ancMenu);
    }

    private async Task SetAncFromTrayAsync(NoiseCancellingMode mode)
    {
        try
        {
            await _anc.SetModeAsync(mode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip("降噪切换失败", ex.Message, BalloonIcon.Error);
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
        var item = new System.Windows.Controls.MenuItem { Header = text };
        item.Click += handler;
        _icon.ContextMenu.Items.Add(item);
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void Dispose()
    {
        _ancSubscription.Dispose();
        _icon.Dispose();
    }
}
