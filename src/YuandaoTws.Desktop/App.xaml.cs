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
        try
        {
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
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            collection.AddLogging(builder => builder.AddSerilog(dispose: true));
            collection.AddApplication();
            collection.AddInfrastructure();
            collection.AddSingleton<Services.WindowsStartupService>();
            collection.AddSingleton<Services.DesktopPreferencesService>();
            collection.AddSingleton<Services.WindowsColorPickerService>();
            collection.AddSingleton<Services.DesktopThemeService>();
            collection.AddSingleton<DashboardViewModel>();
            collection.AddSingleton<Services.WindowBackdropService>();
            collection.AddSingleton<MainWindow>();
            collection.AddSingleton<Services.ConnectionToastService>();
            collection.AddSingleton<Services.TrayIconService>();
            collection.AddSingleton<Services.TaskbarOverlayService>();
            _services = collection.BuildServiceProvider();
            _services.GetRequiredService<Services.DesktopThemeService>().Apply();
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
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用启动失败");
            _services?.Dispose();
            _singleInstance?.Dispose();
            _services = null;
            _singleInstance = null;
            Shutdown(-1);
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

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UI 线程发生未处理异常，已阻止主程序退出");
        e.Handled = true;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "后台线程发生未处理异常");
        }
        else
        {
            Log.Fatal("后台线程发生未知未处理异常：{ExceptionObject}", e.ExceptionObject);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "后台任务发生未观察异常");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _singleInstance?.Dispose();
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
