using Microsoft.Win32;
using System.IO;

namespace YuandaoTws.Desktop.Services;

/// <summary>管理当前 Windows 用户的开机启动项，不需要管理员权限。</summary>
public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YuandaoTws";
    private const string StartupArgument = " --startup";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    public bool SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(ValueName, $"\"{ResolveExecutablePath()}\"{StartupArgument}", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        return IsEnabled == enabled;
    }

    private static string ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        var processName = Path.GetFileName(processPath);
        if (!string.Equals(processName, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath ?? throw new InvalidOperationException("无法确定当前程序路径。");
        }

        var developmentExecutable = Path.Combine(AppContext.BaseDirectory, "YuandaoTws.Desktop.exe");
        return File.Exists(developmentExecutable)
            ? developmentExecutable
            : throw new InvalidOperationException("开发运行时找不到 YuandaoTws.Desktop.exe，请使用已发布版本设置开机启动。");
    }
}
