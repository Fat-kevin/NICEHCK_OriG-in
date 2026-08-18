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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var collection = new ServiceCollection();
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YuandaoTws", "logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(logDirectory, "desktop-.log"), rollingInterval: RollingInterval.Day).CreateLogger();
        collection.AddLogging(builder => builder.AddSerilog(dispose: true));
        collection.AddApplication();
        collection.AddInfrastructure();
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
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
