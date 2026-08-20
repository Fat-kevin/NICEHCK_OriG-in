using System;
using WinRT;

namespace Windows.UI.Composition.Desktop;

[WindowsRuntimeType]
[global::System.Runtime.InteropServices.Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
public interface ICompositorDesktopInterop
{
    void CreateDesktopWindowTarget(IntPtr hwndTarget, bool isTopmost, out IntPtr desktopWindowTarget);
}
