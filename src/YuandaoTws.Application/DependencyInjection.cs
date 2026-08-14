using Microsoft.Extensions.DependencyInjection;
using YuandaoTws.Application.Services;

namespace YuandaoTws.Application;

/// <summary>Application 层 DI 注册入口。</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<HeadsetConnectionService>();
        services.AddSingleton<HeadsetControlService>();
        services.AddSingleton<BatteryMonitorService>();
        services.AddSingleton<NoiseCancellingService>();
        services.AddSingleton<ProtocolProbeService>();
        services.AddSingleton<AutoProbeService>();
        services.AddSingleton<SppProbeService>();
        services.AddSingleton<ProtocolVerifyService>();
        return services;
    }
}
