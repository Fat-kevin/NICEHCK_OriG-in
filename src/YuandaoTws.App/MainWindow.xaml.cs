using System.Windows;
using YuandaoTws.App.ViewModels;

namespace YuandaoTws.App;

/// <summary>主窗口。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}
