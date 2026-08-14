using System.Reactive.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuandaoTws.Application.Services;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly HeadsetConnectionService _connection;
    private readonly HeadsetControlService _control;
    private readonly NoiseCancellingService _anc;
    private readonly IDisposable _subscription;

    [ObservableProperty] private string _deviceName = "未连接设备";
    [ObservableProperty] private string _statusText = "准备连接";
    [ObservableProperty] private string _hintText = "请先在 Windows 蓝牙设置中配对耳机。";
    [ObservableProperty] private string _leftBatteryText = "—";
    [ObservableProperty] private string _rightBatteryText = "—";
    [ObservableProperty] private string _caseBatteryText = "—";
    [ObservableProperty] private string _leftChargeText = "";
    [ObservableProperty] private string _rightChargeText = "";
    [ObservableProperty] private string _ancModeText = "等待连接";
    [ObservableProperty] private string _equalizerText = "等待连接";
    [ObservableProperty] private string _firmwareText = "固件版本 —";
    [ObservableProperty] private bool? _gameModeEnabled;
    [ObservableProperty] private bool? _lowLatencyEnabled;
    [ObservableProperty] private bool? _dualConnectionEnabled;
    [ObservableProperty] private bool? _inEarEnabled;
    [ObservableProperty] private System.Windows.Media.Brush _connectionDotBrush = System.Windows.Media.Brushes.Gray;

    public DashboardViewModel(HeadsetConnectionService connection, HeadsetControlService control, NoiseCancellingService anc)
    {
        _connection = connection; _control = control; _anc = anc;
        _subscription = control.StateChanged.ObserveOn(System.Reactive.Concurrency.DispatcherScheduler.Current).Subscribe(ApplyState);
        connection.StateChanged.Subscribe(state =>
        {
            StatusText = state switch
            {
                HeadsetConnectionState.Connected => "已连接",
                HeadsetConnectionState.Connecting => "连接中",
                HeadsetConnectionState.Reconnecting => "正在重连",
                _ => "未连接"
            };
            ConnectionDotBrush = state == HeadsetConnectionState.Connected ? System.Windows.Media.Brushes.MediumSeaGreen : System.Windows.Media.Brushes.Gray;
        });
    }

    [RelayCommand]
    private async Task ConnectAsync() { HintText = "正在寻找已配对的原点耳机…"; await _connection.StartScanAsync(CancellationToken.None); }

    [RelayCommand]
    private Task DisconnectAsync() => _connection.DisconnectAsync();

    [RelayCommand]
    private async Task SetAncAsync(string mode)
    {
        if (!Enum.TryParse<NoiseCancellingMode>(mode, out var value)) return;
        HintText = "正在确认降噪模式…";
        await _anc.SetModeAsync(value, CancellationToken.None);
    }

    [RelayCommand]
    private async Task SetEqualizerAsync(string preset)
    {
        if (Enum.TryParse<EqualizerPreset>(preset, out var value)) await _control.SetEqualizerAsync(value, CancellationToken.None);
    }

    [RelayCommand]
    private async Task ToggleAsync(string feature)
    {
        if (!Enum.TryParse<HeadsetToggleFeature>(feature, out var value)) return;
        var current = value switch
        {
            HeadsetToggleFeature.GameMode => GameModeEnabled,
            HeadsetToggleFeature.LowLatency => LowLatencyEnabled,
            HeadsetToggleFeature.DualConnection => DualConnectionEnabled,
            HeadsetToggleFeature.InEarDetection => InEarEnabled,
            _ => false
        };
        await _control.SetToggleAsync(value, current != true, CancellationToken.None);
    }

    private void ApplyState(HeadsetControlState state)
    {
        DeviceName = "原道 OriG in「原点」";
        FirmwareText = state.Firmware is null ? "固件版本 —" : $"固件版本 {state.Firmware}";
        AncModeText = Describe(state.AncMode);
        EqualizerText = Describe(state.Equalizer);
        GameModeEnabled = state.GameModeEnabled; LowLatencyEnabled = state.LowLatencyEnabled;
        DualConnectionEnabled = state.DualConnectionEnabled; InEarEnabled = state.InEarDetectionEnabled;
        if (state.Battery is { } battery) ApplyBattery(battery);
    }

    private void ApplyBattery(BatteryInfo battery)
    {
        LeftBatteryText = FormatPercent(battery.LeftEarPercent); RightBatteryText = FormatPercent(battery.RightEarPercent); CaseBatteryText = FormatPercent(battery.CasePercent);
        LeftChargeText = battery.IsLeftEarCharging == true ? "正在充电" : ""; RightChargeText = battery.IsRightEarCharging == true ? "正在充电" : "";
    }

    private static string FormatPercent(byte? value) => value is null ? "—" : $"{value}%";
    private static string Describe(object? value) => value switch { NoiseCancellingMode.Off => "关闭", NoiseCancellingMode.Transparency => "通透", NoiseCancellingMode.Normal => "普通降噪", NoiseCancellingMode.Deep => "深度降噪", NoiseCancellingMode.Experimental => "试验模式", NoiseCancellingMode.WindSuppression => "风噪抑制", EqualizerPreset.Blue => "悔恨之泪", EqualizerPreset.Balanced => "均衡中正", EqualizerPreset.Bass => "欧美澎湃", EqualizerPreset.Pure => "真律还原", EqualizerPreset.Game => "游戏优化", EqualizerPreset.Fine => "细腻佳音", EqualizerPreset.Vocal => "温婉人声", _ => "等待连接" };
    public void Dispose() { _subscription.Dispose(); }
}
