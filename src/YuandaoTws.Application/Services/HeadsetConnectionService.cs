using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>
/// 正式连接编排：RFCOMM/SPP 是主控制链路；GATT 会话仅保留给诊断工具与标准电量兜底。
/// </summary>
public sealed class HeadsetConnectionService : IDisposable
{
    // 原道「原点」状态服务：连接时主动推送 03 头状态帧；当前未确认充电字段。
    private static readonly Guid YuandaoStatusServiceUuid = new("df21fe2c-2515-4fdb-8886-f12c4d67927c");

    private readonly IBluetoothDeviceScanner _scanner;
    private readonly IGattConnectionFactory _gattConnectionFactory;
    private readonly ISppConnectionFactory _sppConnectionFactory;
    private readonly IDeviceProtocol _protocol;
    private readonly ILogger<HeadsetConnectionService> _logger;
    private readonly Subject<HeadsetDevice> _devicesDiscovered = new();
    private readonly Subject<HeadsetConnectionState> _stateChanged = new();
    private readonly Dictionary<string, HeadsetDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    private IDisposable? _scanSubscription;
    private ISppDeviceSession? _controlSession;
    private ISppDeviceSession? _statusSession;
    private IGattDeviceSession? _gattSession;
    private HeadsetDevice? _lastDevice;
    private CancellationTokenSource? _reconnectCts;
    private int _reconnectAttempt;
    private long _connectionGeneration;

    public HeadsetConnectionService(
        IBluetoothDeviceScanner scanner,
        IGattConnectionFactory gattConnectionFactory,
        ISppConnectionFactory sppConnectionFactory,
        IDeviceProtocol protocol,
        ILogger<HeadsetConnectionService> logger)
    {
        _scanner = scanner;
        _gattConnectionFactory = gattConnectionFactory;
        _sppConnectionFactory = sppConnectionFactory;
        _protocol = protocol;
        _logger = logger;
    }

    public IObservable<HeadsetDevice> DevicesDiscovered => _devicesDiscovered;
    public IObservable<HeadsetConnectionState> StateChanged => _stateChanged;
    public HeadsetConnectionState State { get; private set; } = HeadsetConnectionState.Disconnected;

    /// <summary>正式控制会话；所有日常功能仅使用此 SPP 流。</summary>
    public ISppDeviceSession? CurrentControlSession => _controlSession;

    /// <summary>诊断用 GATT 会话；可能为 null，不能用于日常控制。</summary>
    public IGattDeviceSession? CurrentSession => _gattSession;

    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public event Action<ISppDeviceSession?, long>? ControlSessionChanged;
    public event Action<ISppDeviceSession?>? StatusSessionChanged;
    public event Action<IGattDeviceSession?>? SessionChanged;

    public async Task StartScanAsync(CancellationToken cancellationToken)
    {
        StopScanSubscription();
        _devices.Clear();
        _scanSubscription = _scanner.DevicesDiscovered.Subscribe(OnDeviceDiscovered);
        await _scanner.StartScanAsync(cancellationToken);
    }

    public async Task StopScanAsync()
    {
        StopScanSubscription();
        await _scanner.StopScanAsync();
    }

    public IReadOnlyList<HeadsetDevice> GetScannedDevices() => _devices.Values.ToArray();

    public Task<bool> PairAsync(HeadsetDevice device, CancellationToken cancellationToken) =>
        _gattConnectionFactory.PairAsync(device, cancellationToken);

    public async Task ConnectAsync(HeadsetDevice device, CancellationToken cancellationToken)
    {
        await DisconnectAsync();
        CancelReconnect();
        _lastDevice = device;
        SetState(HeadsetConnectionState.Connecting);

        try
        {
            await OpenSessionsAsync(device, cancellationToken);
            _reconnectAttempt = 0;
            SetState(HeadsetConnectionState.Connected);
        }
        catch
        {
            await ReleaseSessionsAsync();
            SetState(HeadsetConnectionState.Disconnected);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        CancelReconnect();
        await ReleaseSessionsAsync();
        SetState(HeadsetConnectionState.Disconnected);
    }

    private async Task OpenSessionsAsync(HeadsetDevice device, CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        var control = await _sppConnectionFactory.OpenAsync(device, _protocol.ServiceUuid, cancellationToken);
        _controlSession = control;
        control.ConnectionLost += OnControlConnectionLost;
        ControlSessionChanged?.Invoke(control, generation);
        _logger.LogInformation("SPP 控制会话已建立：{Device} / {Service}", device.Name, _protocol.ServiceUuid);

        // 该状态服务不是控制通道，打开失败不能影响主界面连接和 ANC/EQ 功能。
        try
        {
            _statusSession = await _sppConnectionFactory.OpenAsync(device, YuandaoStatusServiceUuid, cancellationToken);
            StatusSessionChanged?.Invoke(_statusSession);
            _logger.LogInformation("原道状态会话已建立：{Device} / {Service}", device.Name, YuandaoStatusServiceUuid);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "未建立原道充电状态会话，继续使用主控 SPP：{Device}", device.Name);
        }

        // GATT 失败不影响正式控制；仅作为探测窗口和标准电量诊断的可选能力。
        try
        {
            _gattSession = await _gattConnectionFactory.ConnectAsync(device, cancellationToken);
            SessionChanged?.Invoke(_gattSession);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "未建立可选 GATT 诊断会话，继续使用 SPP 控制：{Device}", device.Name);
        }
    }

    private async Task ReleaseSessionsAsync()
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        var control = _controlSession;
        _controlSession = null;
        if (control is not null)
        {
            control.ConnectionLost -= OnControlConnectionLost;
            await control.DisposeAsync();
        }

        var status = _statusSession;
        _statusSession = null;
        StatusSessionChanged?.Invoke(null);
        if (status is not null)
        {
            await status.DisposeAsync();
        }

        var gatt = _gattSession;
        _gattSession = null;
        if (gatt is not null)
        {
            await gatt.DisposeAsync();
        }

        ControlSessionChanged?.Invoke(null, generation);
        SessionChanged?.Invoke(null);
    }

    private void OnDeviceDiscovered(HeadsetDevice device)
    {
        if (_devices.TryAdd(device.DeviceId, device))
        {
            _devicesDiscovered.OnNext(device);
        }
    }

    private void OnControlConnectionLost()
    {
        if (_controlSession is null || _lastDevice is null)
        {
            return;
        }

        _logger.LogWarning("SPP 控制连接丢失：{DeviceName}，启动自动重连", _lastDevice.Name);
        _controlSession = null;
        var generation = Interlocked.Increment(ref _connectionGeneration);
        ControlSessionChanged?.Invoke(null, generation);
        SetState(HeadsetConnectionState.Reconnecting);
        // 控制流断开时同步释放辅助状态/GATT 会话，避免重连期间旧状态继续覆盖新连接。
        _ = ReleaseAfterControlLossAsync();
        StartReconnectLoop();
    }

    private async Task ReleaseAfterControlLossAsync()
    {
        try
        {
            await ReleaseSessionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放断开的辅助会话失败");
        }
    }

    private void StartReconnectLoop()
    {
        CancelReconnect();
        _reconnectCts = new CancellationTokenSource();
        _ = Task.Run(() => ReconnectLoopAsync(_reconnectCts.Token));
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var device = _lastDevice;
        if (device is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeSpan.FromSeconds(Math.Min(1 << Math.Min(_reconnectAttempt, 5), 30));
                await Task.Delay(delay, cancellationToken);
                await OpenSessionsAsync(device, cancellationToken);
                _reconnectAttempt = 0;
                SetState(HeadsetConnectionState.Connected);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _reconnectAttempt++;
                _logger.LogWarning(ex, "SPP 重连 {DeviceName} 失败（第 {Attempt} 次）", device.Name, _reconnectAttempt);
                await ReleaseSessionsAsync();
            }
        }
    }

    private void CancelReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
    }

    private void SetState(HeadsetConnectionState state)
    {
        State = state;
        _stateChanged.OnNext(state);
    }

    private void StopScanSubscription()
    {
        _scanSubscription?.Dispose();
        _scanSubscription = null;
    }

    public void Dispose()
    {
        StopScanSubscription();
        CancelReconnect();
        _devicesDiscovered.Dispose();
        _stateChanged.Dispose();
    }
}
