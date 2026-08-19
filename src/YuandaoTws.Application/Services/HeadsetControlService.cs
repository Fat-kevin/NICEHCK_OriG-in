using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using YuandaoTws.Domain;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Exceptions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>单一 SPP 协议管线：分帧、状态归约、命令串行化与写后回查。</summary>
public sealed class HeadsetControlService : IDisposable
{
    private static readonly TimeSpan CommandPacing = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);
    private readonly IDeviceProtocol _protocol;
    private readonly ILogger<HeadsetControlService> _logger;
    private readonly Subject<HeadsetControlState> _stateChanged = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private IDisposable? _dataSubscription;
    private CancellationTokenSource? _sessionCts;
    private Task? _batteryPollingTask;
    private NiceHckFrameParser? _parser;
    private ISppDeviceSession? _session;
    private TaskCompletionSource<NiceHckMessage>? _pendingResponse;
    private ushort? _pendingOpCode;
    private int _disposed;

    public HeadsetControlService(IDeviceProtocol protocol, ILogger<HeadsetControlService> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public HeadsetControlState State { get; private set; } = new();
    public IObservable<HeadsetControlState> StateChanged => _stateChanged;
    public bool IsConnected => _session?.IsConnected == true;

    public async Task AttachAsync(ISppDeviceSession session, long generation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await DetachAsync();
        _session = session;
        _parser = new NiceHckFrameParser();
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _dataSubscription = session.DataReceived.Subscribe(chunk =>
        {
            try
            {
                ProcessChunk(chunk.Value);
            }
            catch (Exception ex)
            {
                // 协议解析或观察者异常不能反向终止 SPP 读循环。
                _logger.LogError(ex, "处理 SPP 控制数据失败");
            }
        });
        await RefreshAsync(_sessionCts.Token);
        StartBatteryPolling(generation, _sessionCts.Token);
    }

    public async Task DetachAsync()
    {
        var sessionCts = _sessionCts;
        sessionCts?.Cancel();
        _sessionCts = null;
        _dataSubscription?.Dispose();
        _dataSubscription = null;
        var pollingTask = _batteryPollingTask;
        _batteryPollingTask = null;
        if (pollingTask is not null)
        {
            try
            {
                await pollingTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "等待电量轮询结束失败");
            }
        }

        sessionCts?.Dispose();
        _parser = null;
        _session = null;
        FailPending(new BluetoothConnectionException("SPP 控制会话已失效。"));
        State = new();
        PublishState(State);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var opCode in new[]
        {
            NiceHckOp.Version, NiceHckOp.Battery, NiceHckOp.AncQuery, NiceHckOp.EqQuery,
            NiceHckOp.GameModeQuery, NiceHckOp.LowLatencyQuery, NiceHckOp.DualConnQuery,
            NiceHckOp.InEarQuery, NiceHckOp.WindSuppressionQuery,
        })
        {
            await QueryAsync(opCode, cancellationToken);
            await Task.Delay(CommandPacing, cancellationToken);
        }
    }

    public Task SetAncAsync(NoiseCancellingMode mode, CancellationToken cancellationToken) =>
        SetThenQueryAsync(_protocol.BuildAncCommand(mode), NiceHckOp.AncQuery, cancellationToken);

    public Task SetEqualizerAsync(EqualizerPreset preset, CancellationToken cancellationToken) =>
        SetThenQueryAsync(_protocol.BuildEqualizerCommand(preset), NiceHckOp.EqQuery, cancellationToken);

    public Task SetToggleAsync(HeadsetToggleFeature feature, bool enabled, CancellationToken cancellationToken) =>
        SetThenQueryAsync(_protocol.BuildToggleCommand(feature, enabled), QueryOp(feature), cancellationToken);

    public async Task SetCodecExperimentalAsync(HeadsetCodec codec, CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            await RequireSession().WriteAsync(_protocol.BuildCodecCommand(codec, State.Firmware), cancellationToken);
            State = State with { LastRequestedCodec = codec, UpdatedAt = DateTimeOffset.Now };
            PublishState(State);
        }
        finally { _commandLock.Release(); }
    }

    private async Task SetThenQueryAsync(byte[] setFrame, ushort queryOpCode, CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            await RequireSession().WriteAsync(setFrame, cancellationToken);
            await Task.Delay(CommandPacing, cancellationToken);
            await QueryCoreAsync(queryOpCode, cancellationToken);
        }
        finally { _commandLock.Release(); }
    }

    private async Task QueryAsync(ushort opCode, CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try { await QueryCoreAsync(opCode, cancellationToken); }
        finally { _commandLock.Release(); }
    }

    private async Task QueryCoreAsync(ushort opCode, CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource<NiceHckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOpCode = opCode;
        _pendingResponse = waiter;
        try
        {
            await RequireSession().WriteAsync(_protocol.BuildQueryCommand(opCode), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseTimeout);
            await waiter.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProtocolException($"等待协议响应 0x{opCode:X4} 超时。");
        }
        finally
        {
            _pendingOpCode = null;
            _pendingResponse = null;
        }
    }

    private void ProcessChunk(byte[] data)
    {
        var parser = _parser;
        if (parser is null) return;
        foreach (var message in parser.Feed(data))
        {
            ApplyUpdate(_protocol.TryParse(message));
            if (_pendingOpCode == message.OpCode)
                _pendingResponse?.TrySetResult(message);
        }
    }

    private void ApplyUpdate(HeadsetProtocolUpdate? update)
    {
        if (update is null) return;
        var state = State with { UpdatedAt = DateTimeOffset.Now };
        if (update.Battery is not null) state = state with { Battery = update.Battery };
        if (update.Firmware is not null) state = state with { Firmware = update.Firmware };
        if (update.AncMode is not null) state = state with { AncMode = update.AncMode };
        if (update.Equalizer is not null) state = state with { Equalizer = update.Equalizer };
        if (update.ToggleFeature is { } feature && update.ToggleValue is { } value)
            state = feature switch
            {
                HeadsetToggleFeature.GameMode => state with { GameModeEnabled = value },
                HeadsetToggleFeature.LowLatency => state with { LowLatencyEnabled = value },
                HeadsetToggleFeature.DualConnection => state with { DualConnectionEnabled = value },
                HeadsetToggleFeature.InEarDetection => state with { InEarDetectionEnabled = value },
                HeadsetToggleFeature.WindSuppression => state with { WindSuppressionEnabled = value },
                _ => state,
            };
        State = state;
        PublishState(State);
    }

    private void StartBatteryPolling(long generation, CancellationToken cancellationToken) => _batteryPollingTask = BatteryPollingAsync(generation, cancellationToken);

    private async Task BatteryPollingAsync(long generation, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { while (await timer.WaitForNextTickAsync(cancellationToken)) await QueryAsync(NiceHckOp.Battery, cancellationToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogDebug(ex, "SPP 电量轮询结束（会话 {Generation}）", generation); }
    }

    private static ushort QueryOp(HeadsetToggleFeature feature) => feature switch
    {
        HeadsetToggleFeature.GameMode => NiceHckOp.GameModeQuery,
        HeadsetToggleFeature.LowLatency => NiceHckOp.LowLatencyQuery,
        HeadsetToggleFeature.DualConnection => NiceHckOp.DualConnQuery,
        HeadsetToggleFeature.InEarDetection => NiceHckOp.InEarQuery,
        HeadsetToggleFeature.WindSuppression => NiceHckOp.WindSuppressionQuery,
        _ => throw new ProtocolException($"未知开关功能 {feature}。"),
    };

    private ISppDeviceSession RequireSession() => _session ?? throw new BluetoothConnectionException("尚未建立 SPP 控制会话。");
    private void FailPending(Exception exception) => _pendingResponse?.TrySetException(exception);

    private void PublishState(HeadsetControlState state)
    {
        try
        {
            _stateChanged.OnNext(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发布耳机控制状态失败");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = DisposeCoreAsync();
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await DetachAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放 SPP 控制服务失败");
        }
        finally
        {
            _commandLock.Dispose();
            _stateChanged.Dispose();
        }
    }
}
