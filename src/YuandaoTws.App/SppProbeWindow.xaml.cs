using System.Windows;
using YuandaoTws.App.ViewModels;

namespace YuandaoTws.App;

/// <summary>SPP/RFCOMM 串口探测窗口：枚举经典蓝牙服务、打开字节流、发送/接收。</summary>
public partial class SppProbeWindow : Window
{
    private readonly SppProbeViewModel _viewModel;

    public SppProbeWindow(SppProbeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        // 每次变为可见时自动初始化：刷新设备 → 预选 → 枚举服务（减少手动操作）。
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            try
            {
                await _viewModel.InitializeAsync();
            }
            catch
            {
                // 初始化失败已由 ViewModel 状态栏展示。
            }
        }
    }

    /// <summary>窗口关闭时关闭 SPP 流并释放订阅（窗口为单例，重开时状态保留、流已清理）。</summary>
    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try
        {
            await _viewModel.CleanupAsync();
        }
        catch
        {
            // 关闭时的清理失败不打断窗口关闭。
        }
    }
}
