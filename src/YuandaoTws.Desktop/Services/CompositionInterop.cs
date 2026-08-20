using System;
using System.Runtime.InteropServices;

namespace Windows.UI.Composition.Desktop;

[ComImport]
[Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICompositorDesktopInterop
{
    [PreserveSig]
    int CreateDesktopWindowTarget(
        IntPtr hwndTarget,
        [MarshalAs(UnmanagedType.Bool)] bool isTopmost,
        out IntPtr desktopWindowTarget);
}
