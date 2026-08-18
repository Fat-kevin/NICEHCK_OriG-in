using System.Reactive.Subjects;
using YuandaoTws.Domain;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>
/// 监听原道独立状态服务的 03 头电量帧。
/// 主控 4E 通道负责控制和百分比；当前实测的 03 帧没有可确认的充电字段，
/// 因此本服务只负责接收状态帧，不把任意字节解释成“充电中”。
/// </summary>
public sealed class YuandaoChargingMonitorService : IDisposable
{
    private readonly HeadsetConnectionService _connection;
    private readonly Subject<BatteryInfo> _batteryChanged = new();
    private IDisposable? _dataSubscription;
    private IDisposable? _controlDataSubscription;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public YuandaoChargingMonitorService(HeadsetConnectionService connection)
    {
        _connection = connection;
        _connection.StatusSessionChanged += OnStatusSessionChanged;
        _connection.ControlSessionChanged += OnControlSessionChanged;
    }

    public IObservable<BatteryInfo> BatteryChanged => _batteryChanged;

    private void OnStatusSessionChanged(ISppDeviceSession? session)
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _dataSubscription?.Dispose();
        _dataSubscription = null;

        if (session is null)
        {
            // 清掉上一条状态，避免辅助会话重开期间继续显示旧的“充电中”。
            _batteryChanged.OnNext(BatteryInfo.FromLeftRight(null, null, null));
            return;
        }

        _dataSubscription = SubscribeYuandaoFrames(session);

        // df21 服务通常会主动推送，但部分固件只在打开后收到查询才会再发一次。
        // 查询失败不影响主控通道；轮询仅用于取得状态快照，不代表能够得到充电状态。
        _pollCts = new CancellationTokenSource();
        _pollTask = PollStatusAsync(session, _pollCts.Token);
    }

    private void OnControlSessionChanged(ISppDeviceSession? session, long generation)
    {
        _controlDataSubscription?.Dispose();
        _controlDataSubscription = session is null ? null : SubscribeYuandaoFrames(session);
    }

    private IDisposable SubscribeYuandaoFrames(ISppDeviceSession session)
    {
        var parser = new YuandaoFrameParser();
        return session.DataReceived.Subscribe(chunk =>
        {
            foreach (var message in parser.Feed(chunk.Value))
            {
                var battery = YuandaoFrameSemantics.TryParseBattery(message);
                if (battery is not null)
                {
                    _batteryChanged.OnNext(battery);
                }
            }
        });
    }

    private static async Task PollStatusAsync(ISppDeviceSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await session.WriteAsync(YuandaoCommands.Query(0x03), cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 辅助服务不稳定时静默退出，不能影响主控连接和界面。
                return;
            }
        }
    }

    public void Dispose()
    {
        _connection.StatusSessionChanged -= OnStatusSessionChanged;
        _connection.ControlSessionChanged -= OnControlSessionChanged;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _dataSubscription?.Dispose();
        _dataSubscription = null;
        _controlDataSubscription?.Dispose();
        _controlDataSubscription = null;
        _batteryChanged.Dispose();
    }
}
