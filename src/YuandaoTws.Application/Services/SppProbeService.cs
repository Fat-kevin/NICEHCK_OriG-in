using Microsoft.Extensions.Logging;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Exceptions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>
/// SPP（经典蓝牙 RFCOMM）协议探测：枚举已配对经典设备、枚举 RFCOMM 服务、
/// 打开/关闭双向字节流并读写。与 GATT 探测相互独立，不依赖 BLE 连接。
/// </summary>
public sealed class SppProbeService
{
    private readonly IRfcommServiceEnumerator _enumerator;
    private readonly ISppConnectionFactory _factory;
    private readonly ILogger<SppProbeService> _logger;

    public SppProbeService(
        IRfcommServiceEnumerator enumerator,
        ISppConnectionFactory factory,
        ILogger<SppProbeService> logger)
    {
        _enumerator = enumerator;
        _factory = factory;
        _logger = logger;
    }

    /// <summary>当前打开的 SPP 流会话；未打开时为 null。</summary>
    public ISppDeviceSession? CurrentSession { get; private set; }

    /// <summary>当前会话收到的数据块流；未打开时为 null。</summary>
    public IObservable<SppDataReceived>? DataReceived => CurrentSession?.DataReceived;

    /// <summary>枚举本机已配对的经典蓝牙设备。</summary>
    public Task<IReadOnlyList<HeadsetDevice>> EnumeratePairedDevicesAsync(CancellationToken cancellationToken)
        => _enumerator.EnumeratePairedDevicesAsync(cancellationToken);

    /// <summary>枚举某设备的 RFCOMM 服务（SDP）。</summary>
    public Task<IReadOnlyList<RfcommServiceInfo>> EnumerateServicesAsync(
        HeadsetDevice device,
        CancellationToken cancellationToken)
        => _enumerator.GetServicesAsync(device, cancellationToken);

    /// <summary>打开设备的指定 RFCOMM 服务的 SPP 流（先关闭旧会话）。</summary>
    public async Task OpenAsync(HeadsetDevice device, Guid serviceId, CancellationToken cancellationToken)
    {
        await CloseAsync();
        CurrentSession = await _factory.OpenAsync(device, serviceId, cancellationToken);
        _logger.LogInformation("SPP 会话已建立：{Device} / {ServiceId}", device.Name, serviceId);
    }

    /// <summary>关闭当前 SPP 流会话（若无会话则空操作）。</summary>
    public async Task CloseAsync()
    {
        var session = CurrentSession;
        if (session is null)
        {
            return;
        }

        CurrentSession = null;
        await session.DisposeAsync();
    }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        => RequireSession().WriteAsync(data, cancellationToken);

    private ISppDeviceSession RequireSession() =>
        CurrentSession ?? throw new BluetoothConnectionException("未打开 SPP 流，无法发送。");
}
