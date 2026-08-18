using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuandaoTws.Application;
using YuandaoTws.Infrastructure;
using YuandaoTws.Desktop.ViewModels;

namespace YuandaoTws.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private Services.SingleInstanceGuard? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!Services.SingleInstanceGuard.TryAcquire(out var singleInstance))
        {
            Shutdown();
            return;
        }

        var instanceGuard = singleInstance ?? throw new InvalidOperationException("无法创建单实例保护。");
        _singleInstance = instanceGuard;

        var collection = new ServiceCollection();
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YuandaoTws", "logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(logDirectory, "desktop-.log"), rollingInterval: RollingInterval.Day).CreateLogger();
        collection.AddLogging(builder => builder.AddSerilog(dispose: true));
        collection.AddApplication();
        collection.AddInfrastructure();
        collection.AddSingleton<Services.WindowsStartupService>();
        collection.AddSingleton<DashboardViewModel>();
        collection.AddSingleton<Services.WindowBackdropService>();
        collection.AddSingleton<MainWindow>();
        collection.AddSingleton<Services.ConnectionToastService>();
        collection.AddSingleton<Services.TrayIconService>();
        collection.AddSingleton<Services.TaskbarOverlayService>();
        _services = collection.BuildServiceProvider();
        var mainWindow = _services.GetRequiredService<MainWindow>();
        _ = _services.GetRequiredService<Services.ConnectionToastService>();
        _ = _services.GetRequiredService<Services.TrayIconService>();
        _ = _services.GetRequiredService<Services.TaskbarOverlayService>();
        instanceGuard.Start(() => Dispatcher.InvokeAsync(() => ShowMainWindow(mainWindow)));
        mainWindow.Show();
        if (e.Args.Any(static argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)))
        {
            mainWindow.Hide();
        }
    }

    private static void ShowMainWindow(MainWindow mainWindow)
    {
        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
