using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using YuandaoTws.Desktop.Services;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop;

public partial class MainWindow : Window
{
    private readonly WindowBackdropService _backdrop;
    private readonly DesktopThemeService _theme;
    private HwndSource? _source;
    private bool _deviceAnimationStarted;

    public DashboardViewModel ViewModel { get; }

    public MainWindow(DashboardViewModel viewModel, WindowBackdropService backdrop, DesktopThemeService theme)
    {
        ViewModel = viewModel;
        _backdrop = backdrop;
        _theme = theme;
        // XAML 控件在 InitializeComponent 期间可能触发 ValueChanged，依赖项必须先就绪。
        InitializeComponent();
        DataContext = ViewModel;
        ConfigureDeviceIllustrations();
        Loaded += StartDeviceIllustrations;
        SourceInitialized += OnSourceInitialized;
        _theme.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _backdrop.Apply(this);
        _backdrop.ApplyTheme(this, _theme.IsDark);
        _source = HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        _source?.AddHook(ThemeWindowProc);
    }

    private IntPtr ThemeWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x001A)
        {
            _theme.Apply();
        }
        return IntPtr.Zero;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _backdrop.ApplyTheme(this, _theme.IsDark);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _theme.ThemeChanged -= OnThemeChanged;
        Closed -= OnClosed;
    }

    private void ConfigureDeviceIllustrations()
    {
        LeftEarbudImage.Source = LoadAsset("yuandao-earbud-left.png");
        RightEarbudImage.Source = LoadAsset("yuandao-earbud-right.png");
        ChargingCaseImage.Source = LoadAsset("yuandao-charging-case.png");
    }

    private static ImageSource? LoadAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 900;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void StartDeviceIllustrations(object sender, RoutedEventArgs e)
    {
        if (_deviceAnimationStarted)
        {
            return;
        }

        _deviceAnimationStarted = true;
        AnimateDevicePart(LeftEarbudImage, LeftEarbudScale, LeftEarbudTilt, LeftEarbudFloat, -4, TimeSpan.FromMilliseconds(0));
        AnimateDevicePart(RightEarbudImage, RightEarbudScale, RightEarbudTilt, RightEarbudFloat, 4, TimeSpan.FromMilliseconds(140));
        AnimateDevicePart(ChargingCaseImage, ChargingCaseScale, ChargingCaseTilt, ChargingCaseFloat, 0, TimeSpan.FromMilliseconds(260));
    }

    private static void AnimateDevicePart(
        System.Windows.Controls.Image image,
        ScaleTransform scale,
        RotateTransform tilt,
        TranslateTransform floating,
        double restingAngle,
        TimeSpan delay)
    {
        if (image.Source is null)
        {
            image.Visibility = Visibility.Collapsed;
            return;
        }

        image.Opacity = 0;
        scale.ScaleX = 0.82;
        scale.ScaleY = 0.82;
        tilt.Angle = restingAngle - 8;
        floating.Y = 12;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = delay,
            EasingFunction = ease,
        });

        var enterScale = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(620))
        {
            BeginTime = delay,
            EasingFunction = ease,
        };
        enterScale.Completed += (_, _) => StartScaleLoop(scale);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, enterScale);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(620))
        {
            BeginTime = delay,
            EasingFunction = ease,
        });

        var enterTilt = new DoubleAnimation(restingAngle - 8, restingAngle, TimeSpan.FromMilliseconds(620))
        {
            BeginTime = delay,
            EasingFunction = ease,
        };
        enterTilt.Completed += (_, _) => StartTiltLoop(tilt, restingAngle);
        tilt.BeginAnimation(RotateTransform.AngleProperty, enterTilt);

        var enterFloat = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(620))
        {
            BeginTime = delay,
            EasingFunction = ease,
        };
        enterFloat.Completed += (_, _) => StartFloatLoop(floating);
        floating.BeginAnimation(TranslateTransform.YProperty, enterFloat);
    }

    private static void StartScaleLoop(ScaleTransform scale)
    {
        var animation = new DoubleAnimation(1, 1.045, TimeSpan.FromMilliseconds(3000))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static void StartTiltLoop(RotateTransform tilt, double restingAngle)
    {
        tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(restingAngle - 3, restingAngle + 3, TimeSpan.FromMilliseconds(3600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private static void StartFloatLoop(TranslateTransform floating)
    {
        floating.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2, 3, TimeSpan.FromMilliseconds(2600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
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
