using System.Reactive.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuandaoTws.Application.Services;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Models;
using YuandaoTws.Desktop.Services;

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
    private readonly WindowsStartupService _startup;
    private readonly DesktopPreferencesService _preferences;
    private readonly WindowsColorPickerService _colorPicker;
    private readonly IDisposable _controlSubscription;
    private readonly IDisposable _deviceSubscription;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _chargingSubscription;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private long _lastAttachedGeneration = -1;
    private bool _autoConnectionInProgress;
    private string? _connectedDeviceName;
    private bool _hasAuxiliaryBatteryState;
    private bool _leftCharging;
    private bool _rightCharging;
    private bool _caseCharging;
    private int _disposed;

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
    [ObservableProperty] private bool _casePresent;
    [ObservableProperty] private string _leftChargeText = "";
    [ObservableProperty] private string _rightChargeText = "";
    [ObservableProperty] private string _caseChargeText = "";
    [ObservableProperty] private string _leftChargingStatusText = "充电状态未知";
    [ObservableProperty] private string _rightChargingStatusText = "充电状态未知";
    [ObservableProperty] private string _caseChargingStatusText = "充电状态未知";
    [ObservableProperty] private SolidColorBrush _leftChargingStatusBrush = MutedBrush();
    [ObservableProperty] private SolidColorBrush _rightChargingStatusBrush = MutedBrush();
    [ObservableProperty] private SolidColorBrush _caseChargingStatusBrush = MutedBrush();
    [ObservableProperty] private string _chargingStatusText = "充电状态待同步";
    [ObservableProperty] private double _leftBatteryValue;
    [ObservableProperty] private double _rightBatteryValue;
    [ObservableProperty] private double _caseBatteryValue;
    [ObservableProperty] private string _firmwareText = "—";
    [ObservableProperty] private bool _gameModeEnabled;
    [ObservableProperty] private bool _lowLatencyEnabled;
    [ObservableProperty] private bool _dualConnectionEnabled;
    [ObservableProperty] private bool _inEarEnabled;
    [ObservableProperty] private bool _windSuppressionEnabled;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _taskbarWidgetEnabled;
    [ObservableProperty] private string _batteryAccentColor = "#46A8EC";
    [ObservableProperty] private string _chargingColor = "#50E5A0";
    [ObservableProperty] private SolidColorBrush _batteryAccentBrush = BatteryColorResolver.Brush("#46A8EC", BatteryColorResolver.LowColor);
    [ObservableProperty] private SolidColorBrush _chargingColorBrush = BatteryColorResolver.Brush("#50E5A0", BatteryColorResolver.LowColor);
    [ObservableProperty] private SolidColorBrush _leftBatteryBrush = BatteryColorResolver.Brush("#46A8EC", BatteryColorResolver.LowColor);
    [ObservableProperty] private SolidColorBrush _rightBatteryBrush = BatteryColorResolver.Brush("#46A8EC", BatteryColorResolver.LowColor);
    [ObservableProperty] private SolidColorBrush _caseBatteryBrush = BatteryColorResolver.Brush("#46A8EC", BatteryColorResolver.LowColor);
    [ObservableProperty] private string _ancDetailText = "尚未连接耳机";
    [ObservableProperty] private string _ancStatusText = "降噪状态未知";
    [ObservableProperty] private string _equalizerDetailText = "尚未连接耳机";
    [ObservableProperty] private string _connectionSubText = "正在自动查找原点耳机…";
    [ObservableProperty] private SolidColorBrush _connectionDotBrush = new(System.Windows.Media.Color.FromRgb(0xA6, 0xAC, 0xB4));

    public DashboardViewModel(
        HeadsetConnectionService connection,
        HeadsetControlService control,
        NoiseCancellingService anc,
        YuandaoChargingMonitorService chargingMonitor,
        WindowsStartupService startup,
        DesktopPreferencesService preferences,
        WindowsColorPickerService colorPicker)
    {
        _connection = connection;
        _control = control;
        _anc = anc;
        _startup = startup;
        _preferences = preferences;
        _colorPicker = colorPicker;
        StartWithWindows = startup.IsEnabled;
        ApplyDesktopPreferences(preferences.Current);
        _controlSubscription = control.StateChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(ApplyState);
        _stateSubscription = connection.StateChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(ApplyConnectionState);
        _chargingSubscription = chargingMonitor.BatteryChanged
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(ApplyAuxiliaryBattery);
        _deviceSubscription = connection.DevicesDiscovered
            .ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current)
            .Subscribe(OnDeviceDiscovered);
        connection.ControlSessionChanged += OnControlSessionChanged;
        _ = StartAutoConnectionAsync();
    }

    /// <summary>仅在断线重连时使用；启动时由 <see cref="StartAutoConnectionAsync"/> 统一触发。</summary>
    public Task ForceReconnectAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return StartAutoConnectionAsync();
    }

    private async Task StartAutoConnectionAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

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
            await _connection.StartScanAsync(_lifetimeCts.Token);
            ConnectionSubText = "已发现目标，正在建立连接…";
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
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
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!IsYuandaoDevice(device))
        {
            return;
        }

        ConnectionSubText = $"已找到 {device.Name}，正在连接…";
        _ = ConnectDiscoveredDeviceAsync(device);
    }

    private async Task ConnectDiscoveredDeviceAsync(HeadsetDevice device)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (IsConnected || _connection.State is HeadsetConnectionState.Connecting or HeadsetConnectionState.Reconnecting)
        {
            return;
        }

        try
        {
            _connectedDeviceName = device.Name;
            await _connection.ConnectAsync(device, _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"自动连接失败：{ex.Message}";
            // 失败后稍作等待，让后续广播继续触发重试，避免高频抖动。
            await Task.Delay(TimeSpan.FromSeconds(3), _lifetimeCts.Token);
        }
    }

    private static bool IsYuandaoDevice(HeadsetDevice device) =>
        device.Name.Contains("YUANDAO", StringComparison.OrdinalIgnoreCase)
        || device.Name.Contains("OriG", StringComparison.OrdinalIgnoreCase);

    private void OnControlSessionChanged(ISppDeviceSession? session, long generation)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

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
            await _control.AttachAsync(session, generation, _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await DispatcherInvokeAsync(() => ConnectionSubText = $"读取耳机状态失败：{ex.Message}");
        }
    }

    private void ApplyConnectionState(HeadsetConnectionState state)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

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

        ConnectionSubText = state switch
        {
            HeadsetConnectionState.Connected => "设备已连接，正在同步状态…",
            HeadsetConnectionState.Connecting => "正在建立蓝牙连接…",
            HeadsetConnectionState.Reconnecting => "连接中断，正在重新连接…",
            _ => "正在自动查找原点耳机…",
        };

        if (state == HeadsetConnectionState.Disconnected)
        {
            // 充电图标只代表当前这次协议上报；断开后不能保留上一次会话的状态。
            LeftChargeText = "";
            RightChargeText = "";
            CaseChargeText = "";
            _hasAuxiliaryBatteryState = false;
            _leftCharging = false;
            _rightCharging = false;
            _caseCharging = false;
            ChargingStatusText = "未连接";
            UpdateChargeIndicators();
            DeviceName = "未连接耳机";
            DeviceSubText = "等待连接…";
            FirmwareText = "—";
            LeftBatteryText = "—";
            RightBatteryText = "—";
            CaseBatteryText = "—";
            CasePresent = false;
            LeftBatteryValue = 0;
            RightBatteryValue = 0;
            CaseBatteryValue = 0;
            UpdateBatteryBrushes();
            AncMode = NoiseCancellingMode.Unknown;
            Equalizer = EqualizerPreset.Unknown;
            AncDetailText = "尚未连接耳机";
            AncStatusText = "降噪状态未知";
            EqualizerDetailText = "尚未连接耳机";
            _ = StartAutoConnectionAsync();
        }
    }

    private void ApplyState(HeadsetControlState state)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        DeviceName = string.IsNullOrEmpty(_connectedDeviceName) ? "原道 OriG in「原点」" : _connectedDeviceName;
        DeviceSubText = state.Firmware is { } firmware ? $"固件 {firmware}" : "固件 —";
        FirmwareText = state.Firmware?.ToString() ?? "—";
        if (IsConnected && state.Firmware is not null)
        {
            ConnectionSubText = "设备已连接，状态已同步。";
        }
        AncMode = state.AncMode ?? NoiseCancellingMode.Unknown;
        Equalizer = state.Equalizer ?? EqualizerPreset.Unknown;
        AncDetailText = Describe(state.AncMode);
        AncStatusText = state.AncMode switch
        {
            NoiseCancellingMode.Off => "降噪已关闭",
            NoiseCancellingMode.Unknown or null => "降噪状态未知",
            _ => $"降噪已开启 · {Describe(state.AncMode)}",
        };
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
        CasePresent = battery.CasePercent.HasValue;
        LeftBatteryValue = battery.LeftEarPercent ?? 0;
        RightBatteryValue = battery.RightEarPercent ?? 0;
        CaseBatteryValue = battery.CasePercent ?? 0;
        UpdateBatteryBrushes();
        // 主控 4E 帧没有在两个公开实现中定义充电字段；若状态服务已提供真实值，不能被 4E 查询覆盖。
        if (!_hasAuxiliaryBatteryState)
        {
            if (battery.IsLeftEarCharging is { } left) _leftCharging = left;
            if (battery.IsRightEarCharging is { } right) _rightCharging = right;
            if (battery.IsCaseCharging is { } chargingCase) _caseCharging = chargingCase;
            UpdateChargeIndicators();
        }
    }

    /// <summary>状态服务的 03 帧补充充电标志；当前原道协议没有确认该字段时保持未知。</summary>
    private void ApplyAuxiliaryBattery(BatteryInfo battery)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var hasChargingFlags = battery.IsLeftEarCharging.HasValue
            || battery.IsRightEarCharging.HasValue
            || battery.IsCaseCharging.HasValue;
        if (!hasChargingFlags)
        {
            _hasAuxiliaryBatteryState = false;
            _leftCharging = false;
            _rightCharging = false;
            _caseCharging = false;
            UpdateChargeIndicators();
            ChargingStatusText = "充电状态未知";
            return;
        }

        _hasAuxiliaryBatteryState = true;
        if (battery.IsLeftEarCharging is { } left) _leftCharging = left;
        if (battery.IsRightEarCharging is { } right) _rightCharging = right;
        if (battery.IsCaseCharging is { } chargingCase) _caseCharging = chargingCase;
        UpdateChargeIndicators();
        ChargingStatusText = _leftCharging || _rightCharging || _caseCharging ? "正在充电" : "未在充电";
    }

    private void UpdateChargeIndicators()
    {
        LeftChargeText = _leftCharging ? "⚡" : "";
        RightChargeText = _rightCharging ? "⚡" : "";
        CaseChargeText = _caseCharging ? "⚡" : "";
        LeftChargingStatusText = FormatChargingStatus(_hasAuxiliaryBatteryState, _leftCharging);
        RightChargingStatusText = FormatChargingStatus(_hasAuxiliaryBatteryState, _rightCharging);
        CaseChargingStatusText = FormatChargingStatus(_hasAuxiliaryBatteryState, _caseCharging);
        LeftChargingStatusBrush = ChargingBrush(_hasAuxiliaryBatteryState, _leftCharging);
        RightChargingStatusBrush = ChargingBrush(_hasAuxiliaryBatteryState, _rightCharging);
        CaseChargingStatusBrush = ChargingBrush(_hasAuxiliaryBatteryState, _caseCharging);
        UpdateBatteryBrushes();
    }

    private static string FormatChargingStatus(bool hasStatus, bool charging) =>
        !hasStatus ? "充电状态未知" : charging ? "充电中" : "未在充电";

    private static SolidColorBrush ChargingBrush(bool hasStatus, bool charging) =>
        charging ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A)) : MutedBrush();

    private static SolidColorBrush MutedBrush() =>
        new(System.Windows.Media.Color.FromRgb(0x7D, 0x8D, 0xA1));

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

    /// <summary>设置当前用户的开机启动；启动后默认隐藏到通知区域，避免打扰桌面。</summary>
    [RelayCommand]
    private void SetStartup(object? value)
    {
        var enabled = value is bool requested ? requested : StartWithWindows;
        try
        {
            if (!_startup.SetEnabled(enabled))
            {
                throw new InvalidOperationException("Windows 没有接受开机启动设置。");
            }

            StartWithWindows = enabled;
        }
        catch (Exception ex)
        {
            StartWithWindows = !enabled;
            ConnectionSubText = $"开机启动设置失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void SetTaskbarWidget(object? value)
    {
        var enabled = value is bool requested ? requested : TaskbarWidgetEnabled;
        try
        {
            _preferences.Update(preferences => preferences.TaskbarWidgetEnabled = enabled);
            TaskbarWidgetEnabled = enabled;
        }
        catch (Exception ex)
        {
            TaskbarWidgetEnabled = !enabled;
            ConnectionSubText = $"桌面显示设置失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ChooseBatteryAccent()
    {
        var selected = _colorPicker.Pick(BatteryAccentColor);
        if (selected is null)
        {
            return;
        }

        SaveColor(preferences => preferences.BatteryAccentColor = selected);
    }

    [RelayCommand]
    private void ChooseChargingColor()
    {
        var selected = _colorPicker.Pick(ChargingColor);
        if (selected is null)
        {
            return;
        }

        SaveColor(preferences => preferences.ChargingColor = selected);
    }

    [RelayCommand]
    private void ResetBatteryColors()
    {
        try
        {
            _preferences.ResetColors();
            ApplyDesktopPreferences(_preferences.Current);
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"颜色设置失败：{ex.Message}";
        }
    }

    private void SaveColor(Action<DesktopPreferences> update)
    {
        try
        {
            _preferences.Update(update);
            ApplyDesktopPreferences(_preferences.Current);
        }
        catch (Exception ex)
        {
            ConnectionSubText = $"颜色设置失败：{ex.Message}";
        }
    }

    private void ApplyDesktopPreferences(DesktopPreferences preferences)
    {
        TaskbarWidgetEnabled = preferences.TaskbarWidgetEnabled;
        BatteryAccentColor = preferences.BatteryAccentColor;
        ChargingColor = preferences.ChargingColor;
        BatteryAccentBrush = BatteryColorResolver.Brush(BatteryAccentColor, BatteryColorResolver.LowColor);
        ChargingColorBrush = BatteryColorResolver.Brush(ChargingColor, BatteryColorResolver.LowColor);
        UpdateBatteryBrushes();
    }

    private void UpdateBatteryBrushes()
    {
        var preferences = _preferences.Current;
        LeftBatteryBrush = new SolidColorBrush(BatteryColorResolver.Resolve(
            IsConnected && LeftBatteryText != "—" ? LeftBatteryValue : null,
            IsConnected,
            _leftCharging,
            preferences));
        RightBatteryBrush = new SolidColorBrush(BatteryColorResolver.Resolve(
            IsConnected && RightBatteryText != "—" ? RightBatteryValue : null,
            IsConnected,
            _rightCharging,
            preferences));
        CaseBatteryBrush = new SolidColorBrush(BatteryColorResolver.Resolve(
            IsConnected && CaseBatteryText != "—" ? CaseBatteryValue : null,
            IsConnected,
            _caseCharging,
            preferences));
    }

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCts.Cancel();
        _connection.ControlSessionChanged -= OnControlSessionChanged;
        _controlSubscription.Dispose();
        _deviceSubscription.Dispose();
        _stateSubscription.Dispose();
        _chargingSubscription.Dispose();
        _lifetimeCts.Dispose();
    }
}
