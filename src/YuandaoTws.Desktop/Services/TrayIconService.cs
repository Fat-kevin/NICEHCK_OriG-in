using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon = new();
    private readonly DashboardViewModel _viewModel;
    private readonly MainWindow _mainWindow;

    public TrayIconService(DashboardViewModel viewModel, MainWindow mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _icon.Icon = SystemIcons.Application;
        _icon.ToolTipText = "原点耳机控制";
        _icon.ContextMenu = new System.Windows.Controls.ContextMenu();
        AddMenuItem("打开控制面板", (_, _) => ShowMainWindow());
        AddMenuItem("重新连接耳机", async (_, _) => await _viewModel.ForceReconnectAsync());
        AddMenuItem("退出", (_, _) => System.Windows.Application.Current.Shutdown());
        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

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

    public void Dispose() => _icon.Dispose();
}
