using System.IO;
using System.Runtime.CompilerServices;

namespace YuandaoTws.Desktop;

/// <summary>修复极少数精简启动环境缺失 WINDIR 时 WPF 字体缓存无法初始化的问题。</summary>
internal static class StartupCompatibility
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")))
        {
            return;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            windowsDirectory = Path.GetDirectoryName(Environment.SystemDirectory) ?? @"C:\Windows";
        }

        Environment.SetEnvironmentVariable("windir", windowsDirectory);
    }
}
