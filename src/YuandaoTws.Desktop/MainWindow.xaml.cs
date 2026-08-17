using System.Windows;
using System.Windows.Input;
using YuandaoTws.Desktop.Services;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop;

public partial class MainWindow : Window
{
    private readonly WindowBackdropService _backdrop;

    public DashboardViewModel ViewModel { get; }

    public MainWindow(DashboardViewModel viewModel, WindowBackdropService backdrop)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _backdrop = backdrop;
        DataContext = ViewModel;
        SourceInitialized += (_, _) => _backdrop.Apply(this);
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseWindow(object sender, RoutedEventArgs e) => Hide();
}
