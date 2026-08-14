using System.Windows;
using System.Windows.Input;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop;

public partial class MainWindow : Window
{
    public DashboardViewModel ViewModel { get; }
    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }
    private void DragWindow(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Hide();
}
