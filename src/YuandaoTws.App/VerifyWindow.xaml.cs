using System.Windows;
using YuandaoTws.App.ViewModels;

namespace YuandaoTws.App;

/// <summary>协议自动校验窗口。</summary>
public partial class VerifyWindow : Window
{
    private readonly VerifyViewModel _viewModel;

    public VerifyWindow(VerifyViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>每次变为可见时自动枚举设备并预选（首次打开由主窗口传入 PreselectAddress）。</summary>
    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            IsVisibleChanged -= OnIsVisibleChanged; // 仅首次自动初始化，之后手动刷新。
            await _viewModel.InitializeAsync();
        }
    }
}
