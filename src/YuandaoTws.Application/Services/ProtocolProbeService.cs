using Microsoft.Extensions.Logging;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Exceptions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>
/// 协议探测：枚举设备全部 GATT 服务/特征、读取特征值、订阅通知。
/// 用于协议逆向的第一手数据采集（对应设计文档 FR-10「协议探测模式」）。
/// 本身不持有会话，实时取用 <see cref="HeadsetConnectionService"/> 的当前会话。
/// </summary>
public sealed class ProtocolProbeService
{
    private readonly HeadsetConnectionService _connectionService;
    private readonly ILogger<ProtocolProbeService> _logger;

    public ProtocolProbeService(
        HeadsetConnectionService connectionService,
        ILogger<ProtocolProbeService> logger)
    {
        _connectionService = connectionService;
        _logger = logger;
    }

    /// <summary>当前会话；未连接时为 null。</summary>
    public IGattDeviceSession? CurrentSession => _connectionService.CurrentSession;

    /// <summary>当前会话的全部 Notify 帧流；未连接时为 null。</summary>
    public IObservable<GattNotification>? Notifications => _connectionService.CurrentSession?.Notifications;

    public Task<IReadOnlyList<GattServiceInfo>> EnumerateAsync(CancellationToken cancellationToken)
        => RequireSession().EnumerateServicesAsync(cancellationToken);

    public Task<byte[]?> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken)
        => RequireSession().ReadAsync(characteristicUuid, cancellationToken);

    public Task SubscribeAsync(Guid characteristicUuid, CancellationToken cancellationToken)
        => RequireSession().SubscribeAsync(characteristicUuid, cancellationToken);

    public Task WriteAsync(
        Guid characteristicUuid,
        byte[] data,
        CancellationToken cancellationToken,
        bool withResponse = true)
        => RequireSession().WriteAsync(characteristicUuid, data, cancellationToken, withResponse);

    private IGattDeviceSession RequireSession() =>
        _connectionService.CurrentSession
        ?? throw new BluetoothConnectionException("未连接设备，无法执行协议探测。");
}
