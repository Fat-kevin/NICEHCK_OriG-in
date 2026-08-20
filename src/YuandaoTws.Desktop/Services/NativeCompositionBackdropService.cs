using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.System;
using WinRT;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// Windows Composition 后方毛玻璃：HostBackdropBrush 采样窗口后方内容，
/// Win2D GaussianBlurEffect 在 GPU 上做模糊，再作为桌面窗口视觉树的底层。
/// </summary>
public sealed class NativeCompositionBackdropService : IDisposable
{
    private Compositor? _compositor;
    private DispatcherQueueController? _dispatcherQueueController;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;
    private SpriteVisual? _blurVisual;
    private CompositionEffectBrush? _blurBrush;
    private CompositionRoundedRectangleGeometry? _roundedGeometry;
    private CompositionGeometricClip? _roundedClip;

    public bool IsActive => _target is not null && _blurVisual is not null;

    public bool TryApply(Window window, byte opacity)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            EnsureDispatcherQueue();
            _compositor ??= new Compositor();

            var compositorUnknown = Marshal.GetIUnknownForObject(_compositor);
            IntPtr interopPointer = IntPtr.Zero;
            try
            {
                var interopIid = typeof(ICompositorDesktopInterop).GUID;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(compositorUnknown, ref interopIid, out interopPointer));
                var interop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(interopPointer);
                Marshal.ThrowExceptionForHR(interop.CreateDesktopWindowTarget(hwnd, false, out var targetAbi));
                _target = DesktopWindowTarget.FromAbi(targetAbi);
            }
            finally
            {
                if (interopPointer != IntPtr.Zero)
                {
                    Marshal.Release(interopPointer);
                }

                Marshal.Release(compositorUnknown);
            }

            // HostBackdropBrush 在当前窗口绘制前采样后方其它窗口内容。
            var source = new CompositionEffectSourceParameter("backdrop");
            var blurEffect = new GaussianBlurEffect
            {
                Name = "WindowBackdropBlur",
                BlurAmount = 18f,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = source,
            };
            var effectFactory = _compositor.CreateEffectFactory(blurEffect);
            _blurBrush = effectFactory.CreateBrush();
            _blurBrush.SetSourceParameter("backdrop", _compositor.CreateHostBackdropBrush());

            _blurVisual = _compositor.CreateSpriteVisual();
            _blurVisual.RelativeSizeAdjustment = Vector2.One;
            _blurVisual.Brush = _blurBrush;
            _blurVisual.Opacity = opacity / 255f;

            var root = _compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;
            root.Children.InsertAtTop(_blurVisual);
            _root = root;
            _target.Root = root;
            UpdateClip(window.ActualWidth > 0 ? window.ActualWidth : window.Width,
                window.ActualHeight > 0 ? window.ActualHeight : window.Height,
                26);
            return true;
        }
        catch (Exception)
        {
            DisposeCompositionObjects();
            return false;
        }
    }

    public void SetOpacity(byte opacity)
    {
        if (_blurVisual is not null)
        {
            _blurVisual.Opacity = opacity / 255f;
        }
    }

    /// <summary>将 Composition 背景裁剪到与窗口相同的圆角区域。</summary>
    public void UpdateClip(double width, double height, double radius)
    {
        if (_compositor is null || _root is null || _blurVisual is null || width <= 0 || height <= 0)
        {
            return;
        }

        _roundedGeometry ??= _compositor.CreateRoundedRectangleGeometry();
        _roundedGeometry.Size = new Vector2((float)width, (float)height);
        _roundedGeometry.CornerRadius = new Vector2((float)radius, (float)radius);
        _roundedClip ??= _compositor.CreateGeometricClip(_roundedGeometry);
        // DesktopWindowTarget 下 ContainerVisual 的 Clip 在部分系统版本不会向下裁剪，
        // 直接裁剪实际承载 HostBackdropBrush 的 SpriteVisual 才能消除矩形毛玻璃底。
        _blurVisual.Clip = _roundedClip;
    }

    private void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = DispatcherQueueThreadType.Current,
            ApartmentType = DispatcherQueueApartmentType.STA,
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var controllerAbi));
        _dispatcherQueueController = DispatcherQueueController.FromAbi(controllerAbi);
    }

    public void Dispose()
    {
        DisposeCompositionObjects();
        _dispatcherQueueController = null;
    }

    private void DisposeCompositionObjects()
    {
        if (_target is not null)
        {
            _target.Root = null;
        }

        _root = null;
        _roundedClip = null;
        _roundedGeometry = null;
        _blurVisual = null;
        _blurBrush = null;
        _target = null;
        _compositor = null;
    }

    [DllImport("coremessaging.dll", EntryPoint = "CreateDispatcherQueueController")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        out IntPtr dispatcherQueueController);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public DispatcherQueueThreadType ThreadType;
        public DispatcherQueueApartmentType ApartmentType;
    }

    private enum DispatcherQueueThreadType
    {
        Current = 2,
    }

    private enum DispatcherQueueApartmentType
    {
        STA = 2,
    }
}
