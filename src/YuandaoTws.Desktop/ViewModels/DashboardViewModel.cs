using System.Reactive.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuandaoTws.Application.Services;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Desktop.ViewModels;

/// <summary>
/// 生产版主视图模型：唯一行为是「启动后自动扫描 → 发现目标即自动连接 → 展示并操作耳机状态」。
/// 不暴露任何手动扫描 / 设备列表 / 连接按钮，也不显示调试信息。
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly HeadsetConnectionService _connection;
    private readonly HeadsetControlService _control;
    private readonly NoiseCancellingService _anc;
    private readonly IDisposable _controlSubscription;
    private readonly IDisposable _deviceSubscription;
    private readonly IDisposable _stateSubscription;
    private long _lastAttachedGeneration = -1;
    private bool _autoConnectionInProgress;
    private string? _connectedDeviceName;

    /// <summary>降噪分段选择器绑定源（当前模式）。</summary>
    [ObservableProperty] private NoiseCancellingMode _ancMode = NoiseCancellingMode.Unknown;

    /// <summary>均衡器芯片绑定源（当前预设）。</summary>
    [ObservableProperty] private EqualizerPreset _equalizer = EqualizerPreset.Unknown;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _deviceName = "未连接耳机";
    [ObservableProperty] private string _deviceSubText = "等待连接…";
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _leftBatteryText = "—";
    [ObservableProperty] private string _rightBatteryText = "—";
    [ObservableProperty] private string _caseBatteryText = "—";
    [ObservableProperty] private string _leftChargeText = "";
    [ObservableProperty] private string _rightChargeText = "";
    [ObservableProperty] private double _leftBatteryValue;
    [ObservableProperty] private double _rightBatteryValue;
    [ObservableProperty] private double _caseBatteryValue;
    [ObservableProperty] private string _firmwareText = "—";
    [ObservableProperty] private bool _gameModeEnabled;
    [ObservableProperty] private bool _lowLatencyEnabled;
    [ObservableProperty] private bool _dualConnectionEnabled;
    [ObservableProperty] private bool _inEarEnabled;
    [ObservableProperty] private bool _windSuppressionEnabled;
    [ObservableProperty] private string _ancDetailText = "尚未连接耳机";
    [ObservableProperty] private string _equalizerDetailText = "尚未连接耳机";
    [ObservableProperty] private string _connectionSubText = "正在自动查找原点耳机…";
    [ObservableProperty] private SolidColorBrush _connectionDotBrush = new(System.Windows.Media.Color.FromRgb(0xA6, 0xAC, 0xB4));

    public DashboardViewModel(
        HeadsetConnectionService connection,
        HeadsetControlService control,
        NoiseCancellingService anc)
    {
        _connection = connection;
        _control = control;
        _anc = anc;
        _controlSubscription = control.StateChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(ApplyState);
        _stateSubscription = connection.StateChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(ApplyConnectionState);
        _deviceSubscription = connection.DevicesDiscovered
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(OnDeviceDiscovered);
        connection.ControlSessionChanged += OnControlSessionChanged;
        _ = StartAutoConnectionAsync();
    }

    /// <summary>仅在断线重连时使用；启动时由 <see cref="StartAutoConnectionAsync"/> 统一触发。</summary>
    public Task ForceReconnectAsync() => StartAutoConnectionAsync();

    private async Task StartAutoConnectionAsync()
    {
        if (_autoConnectionInProgress)
        {
            return;
        }

        // 已连接或正在建立连接时不再重复扫描；若处于未连接状态则先释放旧会话。
        if (IsConnected || _connection.State is HeadsetConnectionState.Connecting or HeadsetConnectionState.Reconnecting)
        {
            return;
        }

        _autoConnectionInProgress = true;
        try
        {
            IsSearching = true;
            ConnectionSubText = "正在自动查找原点耳机…";
            await _connection.StartScanAsync(CancellationToken.None);
            ConnectionSubText = "已发现目标，正在建立连接…";
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"自动查找失败：{ex.Message}";
        }
        finally
        {
            _autoConnectionInProgress = false;
        }
    }

    private void OnDeviceDiscovered(HeadsetDevice device)
    {
        if (!IsYuandaoDevice(device))
        {
            return;
        }

        ConnectionSubText = $"已找到 {device.Name}，正在连接…";
        _ = ConnectDiscoveredDeviceAsync(device);
    }

    private async Task ConnectDiscoveredDeviceAsync(HeadsetDevice device)
    {
        if (IsConnected || _connection.State is HeadsetConnectionState.Connecting or HeadsetConnectionState.Reconnecting)
        {
            return;
        }

        try
        {
            _connectedDeviceName = device.Name;
            await _connection.ConnectAsync(device, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"自动连接失败：{ex.Message}";
            // 失败后稍作等待，让后续广播继续触发重试，避免高频抖动。
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    private static bool IsYuandaoDevice(HeadsetDevice device) =>
        device.Name.Contains("YUANDAO", StringComparison.OrdinalIgnoreCase)
        || device.Name.Contains("OriG", StringComparison.OrdinalIgnoreCase);

    private void OnControlSessionChanged(ISppDeviceSession? session, long generation)
    {
        _ = AttachControlSessionAsync(session, generation);
    }

    private async Task AttachControlSessionAsync(ISppDeviceSession? session, long generation)
    {
        try
        {
            if (session is null)
            {
                await _control.DetachAsync();
                return;
            }

            if (generation == _lastAttachedGeneration)
            {
                return;
            }

            _lastAttachedGeneration = generation;
            await _control.AttachAsync(session, generation, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await DispatcherInvokeAsync(() => ConnectionSubText = $"读取耳机状态失败：{ex.Message}");
        }
    }

    private void ApplyConnectionState(HeadsetConnectionState state)
    {
        IsConnected = state == HeadsetConnectionState.Connected;
        IsSearching = state is HeadsetConnectionState.Connecting or HeadsetConnectionState.Reconnecting;
        StatusText = state switch
        {
            HeadsetConnectionState.Connected => "已连接",
            HeadsetConnectionState.Connecting => "正在连接",
            HeadsetConnectionState.Reconnecting => "正在重连",
            _ => "未连接",
        };
        ConnectionDotBrush = state == HeadsetConnectionState.Connected
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xAC, 0xB4));

        if (state == HeadsetConnectionState.Disconnected)
        {
            ConnectionSubText = "连接已断开，正在自动查找原点耳机…";
            _ = StartAutoConnectionAsync();
        }
    }

    private void ApplyState(HeadsetControlState state)
    {
        DeviceName = string.IsNullOrEmpty(_connectedDeviceName) ? "原道 OriG in「原点」" : _connectedDeviceName;
        DeviceSubText = state.Firmware is { } firmware ? $"固件 {firmware}" : "固件 —";
        FirmwareText = state.Firmware?.ToString() ?? "—";
        AncMode = state.AncMode ?? NoiseCancellingMode.Unknown;
        Equalizer = state.Equalizer ?? EqualizerPreset.Unknown;
        AncDetailText = Describe(state.AncMode);
        EqualizerDetailText = Describe(state.Equalizer);
        GameModeEnabled = state.GameModeEnabled == true;
        LowLatencyEnabled = state.LowLatencyEnabled == true;
        DualConnectionEnabled = state.DualConnectionEnabled == true;
        InEarEnabled = state.InEarDetectionEnabled == true;
        WindSuppressionEnabled = state.WindSuppressionEnabled == true;
        if (state.Battery is { } battery)
        {
            ApplyBattery(battery);
        }
    }

    private void ApplyBattery(BatteryInfo battery)
    {
        LeftBatteryText = FormatPercent(battery.LeftEarPercent);
        RightBatteryText = FormatPercent(battery.RightEarPercent);
        CaseBatteryText = FormatPercent(battery.CasePercent);
        LeftBatteryValue = battery.LeftEarPercent ?? 0;
        RightBatteryValue = battery.RightEarPercent ?? 0;
        CaseBatteryValue = battery.CasePercent ?? 0;
        LeftChargeText = battery.IsLeftEarCharging == true ? "充电中" : "";
        RightChargeText = battery.IsRightEarCharging == true ? "充电中" : "";
    }

    /// <summary>降噪分段选择触发：应用目标模式，随后状态流回查确认。</summary>
    [RelayCommand]
    private async Task SetAncAsync(NoiseCancellingMode mode)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            ConnectionSubText = $"正在切换为「{Describe(mode)}」…";
            await _anc.SetModeAsync(mode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"降噪切换失败：{ex.Message}";
        }
        finally
        {
            ConnectionSubText = IsConnected ? "耳机状态已同步。" : ConnectionSubText;
        }
    }

    /// <summary>均衡器芯片选择触发。</summary>
    [RelayCommand]
    private async Task SetEqualizerAsync(EqualizerPreset preset)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            ConnectionSubText = $"正在切换为「{Describe(preset)}」…";
            await _control.SetEqualizerAsync(preset, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"均衡器切换失败：{ex.Message}";
        }
        finally
        {
            ConnectionSubText = IsConnected ? "耳机状态已同步。" : ConnectionSubText;
        }
    }

    /// <summary>开关项触发：IsChecked 双向绑定，用户点击后属性已翻转为目标值，这里直接下发该值。</summary>
    [RelayCommand]
    private async Task SetToggleAsync(string featureName)
    {
        if (!IsConnected)
        {
            return;
        }

        var feature = featureName switch
        {
            nameof(HeadsetToggleFeature.GameMode) => HeadsetToggleFeature.GameMode,
            nameof(HeadsetToggleFeature.LowLatency) => HeadsetToggleFeature.LowLatency,
            nameof(HeadsetToggleFeature.DualConnection) => HeadsetToggleFeature.DualConnection,
            nameof(HeadsetToggleFeature.InEarDetection) => HeadsetToggleFeature.InEarDetection,
            nameof(HeadsetToggleFeature.WindSuppression) => HeadsetToggleFeature.WindSuppression,
            _ => default(HeadsetToggleFeature),
        };
        if (feature == default)
        {
            return;
        }

        var target = feature switch
        {
            HeadsetToggleFeature.GameMode => GameModeEnabled,
            HeadsetToggleFeature.LowLatency => LowLatencyEnabled,
            HeadsetToggleFeature.DualConnection => DualConnectionEnabled,
            HeadsetToggleFeature.InEarDetection => InEarEnabled,
            HeadsetToggleFeature.WindSuppression => WindSuppressionEnabled,
            _ => false,
        };

        // 下发用户刚选择的目标值；耳机回帧会作为唯一事实来源通过状态流再次同步。
        try
        {
            ConnectionSubText = $"正在应用「{ToggleLabel(feature)}」…";
            await _control.SetToggleAsync(feature, target, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"设置失败：{ex.Message}";
        }
        finally
        {
            ConnectionSubText = IsConnected ? "耳机状态已同步。" : ConnectionSubText;
        }
    }

    private static string ToggleLabel(HeadsetToggleFeature feature) => feature switch
    {
        HeadsetToggleFeature.GameMode => "游戏模式",
        HeadsetToggleFeature.LowLatency => "低延迟",
        HeadsetToggleFeature.DualConnection => "双设备连接",
        HeadsetToggleFeature.InEarDetection => "入耳检测",
        HeadsetToggleFeature.WindSuppression => "抗风噪",
        _ => feature.ToString(),
    };

    private static string FormatPercent(byte? value) => value is null ? "—" : $"{value}%";

    private static string Describe(NoiseCancellingMode? mode) => mode switch
    {
        NoiseCancellingMode.Off => "关闭",
        NoiseCancellingMode.Transparency => "通透",
        NoiseCancellingMode.Normal => "普通降噪",
        NoiseCancellingMode.Deep => "深度降噪",
        NoiseCancellingMode.Experimental => "试验模式",
        NoiseCancellingMode.WindSuppression => "风噪抑制",
        _ => "尚未连接耳机",
    };

    private static string Describe(EqualizerPreset? preset) => preset switch
    {
        EqualizerPreset.Blue => "悔恨之泪",
        EqualizerPreset.Balanced => "均衡中正",
        EqualizerPreset.Bass => "欧美澎湃",
        EqualizerPreset.Pure => "真律还原",
        EqualizerPreset.Game => "游戏优化",
        EqualizerPreset.Fine => "细腻佳音",
        EqualizerPreset.Vocal => "温婉人声",
        _ => "尚未连接耳机",
    };

    private static Task DispatcherInvokeAsync(Action action) =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(action).Task;

    public void Dispose()
    {
        _connection.ControlSessionChanged -= OnControlSessionChanged;
        _controlSubscription.Dispose();
        _deviceSubscription.Dispose();
        _stateSubscription.Dispose();
    }
}
