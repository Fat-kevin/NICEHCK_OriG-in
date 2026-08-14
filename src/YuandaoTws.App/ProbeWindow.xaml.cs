using System.Windows;
using YuandaoTws.App.ViewModels;

namespace YuandaoTws.App;

/// <summary>协议探测窗口：枚举 GATT 通道、读值、监听通知、导出报告。</summary>
public partial class ProbeWindow : Window
{
    public ProbeWindow(ProbeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
