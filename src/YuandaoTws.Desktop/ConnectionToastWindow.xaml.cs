using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop;

public partial class ConnectionToastWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly Action _showMainWindow;
    private bool _hasCase;
    private bool _isAnimatingLayout;

    public ConnectionToastWindow(DashboardViewModel viewModel, Action showMainWindow)
    {
        _viewModel = viewModel;
        _showMainWindow = showMainWindow;
        InitializeComponent();
        DataContext = viewModel;
        ToastLeftImage.Source = LoadAsset("yuandao-earbud-left.png");
        ToastRightImage.Source = LoadAsset("yuandao-earbud-right.png");
        ToastCaseImage.Source = LoadAsset("yuandao-charging-case.png");
        ToastLeftImageNoCase.Source = ToastLeftImage.Source;
        ToastRightImageNoCase.Source = ToastRightImage.Source;
        ConfigureTransforms();
        Loaded += (_, _) =>
        {
            PositionAtBottomRight();
            UpdateCaseLayout(_viewModel.CasePresent, animate: false);
            StartFloatingAnimations();
        };
        SizeChanged += (_, _) => PositionAtBottomRight();
    }

    private void OpenMainWindow(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _showMainWindow();
    }

    public void ReplayConnectionAnimation()
    {
        if (!IsVisible)
        {
            Show();
        }

        PositionAtBottomRight();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        if (Shell.RenderTransform is TranslateTransform move)
        {
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(34, 0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    public void UpdateCaseLayout(bool hasCase, bool animate = true)
    {
        if (_hasCase == hasCase && (hasCase ? WithCasePanel.Visibility == Visibility.Visible : WithoutCasePanel.Visibility == Visibility.Visible))
        {
            return;
        }

        if (!animate || !_hasCase && !hasCase)
        {
            ApplyCaseLayoutImmediately(hasCase);
            return;
        }

        if (_isAnimatingLayout)
        {
            StopCaseLayoutAnimations();
            _isAnimatingLayout = false;
        }

        _isAnimatingLayout = true;
        if (!hasCase)
        {
            AnimateCaseOut();
            return;
        }

        _hasCase = true;
        WithCasePanel.Visibility = Visibility.Visible;
        WithoutCasePanel.Visibility = Visibility.Collapsed;
        WithCasePanel.Opacity = 0;
        WithCasePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        _isAnimatingLayout = false;
    }

    private void AnimateCaseOut()
    {
        // CaseItem 使用的是 TransformGroup：第 0 个子变换负责缩放，第 1 个负责横向位移。
        // 不能把整个 RenderTransform 直接转换成 ScaleTransform，否则盒子状态变化时会崩溃。
        if (CaseItem.RenderTransform is not TransformGroup group
            || group.Children.Count < 2
            || group.Children[0] is not ScaleTransform scale
            || group.Children[1] is not TranslateTransform shift)
        {
            ApplyCaseLayoutImmediately(hasCase: false);
            _isAnimatingLayout = false;
            return;
        }

        var ease = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.25 };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            _hasCase = false;
            WithCasePanel.Visibility = Visibility.Collapsed;
            WithoutCasePanel.Visibility = Visibility.Visible;
            WithoutCasePanel.Opacity = 0;
            if (WithoutCasePanel.RenderTransform is ScaleTransform noCaseScale)
            {
                noCaseScale.ScaleX = 0.88;
                noCaseScale.ScaleY = 0.88;
                noCaseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                noCaseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            WithoutCasePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            scale.ScaleX = scale.ScaleY = 1;
            shift.X = 0;
            _isAnimatingLayout = false;
        };
        CaseItem.BeginAnimation(OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.72, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.72, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
    }

    private void ApplyCaseLayoutImmediately(bool hasCase)
    {
        _hasCase = hasCase;
        WithCasePanel.Visibility = hasCase ? Visibility.Visible : Visibility.Collapsed;
        WithoutCasePanel.Visibility = hasCase ? Visibility.Collapsed : Visibility.Visible;
        WithCasePanel.Opacity = hasCase ? 1 : 0;
        WithoutCasePanel.Opacity = hasCase ? 0 : 1;
    }

    private void StopCaseLayoutAnimations()
    {
        CaseItem.BeginAnimation(OpacityProperty, null);
        WithCasePanel.BeginAnimation(OpacityProperty, null);
        WithoutCasePanel.BeginAnimation(OpacityProperty, null);

        if (CaseItem.RenderTransform is TransformGroup group)
        {
            if (group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }

            if (group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } shift)
            {
                shift.BeginAnimation(TranslateTransform.XProperty, null);
            }
        }

        if (WithoutCasePanel.RenderTransform is ScaleTransform noCaseScale)
        {
            noCaseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            noCaseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    private void ConfigureTransforms()
    {
        ToastLeftImage.RenderTransform = new TransformGroup { Children = [new ScaleTransform(0.94, 0.94), new RotateTransform(-3), new TranslateTransform()] };
        ToastRightImage.RenderTransform = new TransformGroup { Children = [new ScaleTransform(0.94, 0.94), new RotateTransform(3), new TranslateTransform()] };
        ToastCaseImage.RenderTransform = new TransformGroup { Children = [new ScaleTransform(0.94, 0.94), new RotateTransform(), new TranslateTransform()] };
        ToastLeftImageNoCase.RenderTransform = new TransformGroup { Children = [new ScaleTransform(0.94, 0.94), new RotateTransform(-3), new TranslateTransform()] };
        ToastRightImageNoCase.RenderTransform = new TransformGroup { Children = [new ScaleTransform(0.94, 0.94), new RotateTransform(3), new TranslateTransform()] };
        CaseItem.RenderTransform = new TransformGroup { Children = [new ScaleTransform(1, 1), new TranslateTransform()] };
        WithoutCasePanel.RenderTransform = new ScaleTransform(1, 1);
        Shell.RenderTransform = new TranslateTransform(0, 0);
    }

    private void StartFloatingAnimations()
    {
        foreach (var image in new[] { ToastLeftImage, ToastRightImage, ToastCaseImage, ToastLeftImageNoCase, ToastRightImageNoCase })
        {
            image.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 1, TimeSpan.Zero));
            if (image.RenderTransform is not TransformGroup group
                || group.Children.Count < 3
                || group.Children[0] is not ScaleTransform scale
                || group.Children[1] is not RotateTransform tilt
                || group.Children[2] is not TranslateTransform floatTransform)
            {
                // 动画属于可选视觉效果；模板结构变化时跳过，不影响连接状态和主窗口。
                continue;
            }

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1.025, TimeSpan.FromMilliseconds(2600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1.025, TimeSpan.FromMilliseconds(2600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(tilt.Angle - 2.5, tilt.Angle + 2.5, TimeSpan.FromMilliseconds(3400)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            floatTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-1.5, 3, TimeSpan.FromMilliseconds(2400)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
    }

    private void PositionAtBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 22;
        Top = workArea.Bottom - ActualHeight - 22;
    }

    private static ImageSource? LoadAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(path)) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 900;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
