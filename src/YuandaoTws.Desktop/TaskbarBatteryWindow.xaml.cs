using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop;

public partial class TaskbarBatteryWindow : Window
{
    private readonly Action _showMainWindow;
    private readonly DispatcherTimer _keepAliveTimer;
    private IntPtr _hwnd;

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public TaskbarBatteryWindow(DashboardViewModel viewModel, Action showMainWindow)
    {
        _showMainWindow = showMainWindow;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => PositionOnTaskbar();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SourceInitialized += OnSourceInitialized;
        _keepAliveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(900), DispatcherPriority.Background, (_, _) => KeepAlive(), Dispatcher);
        _keepAliveTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        EnsureNativeTopmost();
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WorkArea))
        {
            PositionOnTaskbar();
        }
    }

    private void PositionOnTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var taskbarHeight = Math.Max(0, SystemParameters.PrimaryScreenHeight - workArea.Bottom);
        // Windows 11 左侧天气组件后到居中图标前通常是空白区域；把胶囊放在这块区域的中心。
        Left = workArea.Left + workArea.Width * 0.20 - Width / 2;
        Top = taskbarHeight >= Height
            ? workArea.Bottom + (taskbarHeight - Height) / 2
            : workArea.Bottom - Height - 4;
        EnsureNativeTopmost();
    }

    private void KeepAlive()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero)
        {
            return;
        }

        PositionOnTaskbar();
    }

    private void EnsureNativeTopmost()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private void OpenMainWindow(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _showMainWindow();
    }

    protected override void OnClosed(EventArgs e)
    {
        _keepAliveTimer.Stop();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SourceInitialized -= OnSourceInitialized;
        base.OnClosed(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
