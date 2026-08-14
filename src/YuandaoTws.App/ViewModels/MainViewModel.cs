using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YuandaoTws.Application.Services;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Exceptions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.App.ViewModels;

/// <summary>主窗口 ViewModel：设备扫描/连接、电量显示、降噪切换。</summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly HeadsetConnectionService _connectionService;
    private readonly HeadsetControlService _headsetControl;
    private readonly BatteryMonitorService _batteryMonitor;
    private readonly NoiseCancellingService _noiseCancelling;
    private readonly AutoProbeService _autoProbe;
    private readonly IDeviceProtocol _protocol;
    private readonly ProbeWindow _probeWindow;
    private readonly SppProbeWindow _sppProbeWindow;
    private readonly SppProbeViewModel _sppProbeViewModel;
    private readonly VerifyWindow _verifyWindow;
    private readonly VerifyViewModel _verifyViewModel;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly CompositeDisposable _subscriptions = [];

    public ObservableCollection<HeadsetDeviceItem> Devices { get; } = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _canConnect = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private HeadsetDeviceItem? _selectedDevice;

    [ObservableProperty]
    private string _statusText = "未连接";

    [ObservableProperty]
    private string _deviceName = "—";

    [ObservableProperty]
    private string _leftBattery = "--";

    [ObservableProperty]
    private string _rightBattery = "--";

    [ObservableProperty]
    private string _caseBattery = "--";

    [ObservableProperty]
    private bool _ancControlsEnabled;

    [ObservableProperty]
    private string _ancStatusText;

    [ObservableProperty]
    private bool _isAncOff;

    [ObservableProperty]
    private bool _isAncOn;

    [ObservableProperty]
    private bool _isTransparency;

    [ObservableProperty]
    private bool _isAncNormal;

    [ObservableProperty]
    private bool _isAncDeep;

    [ObservableProperty]
    private bool _isAncExperimental;

    [ObservableProperty]
    private bool _isAncWindSuppression;

    [ObservableProperty]
    private string _firmwareVersion = "--";

    [ObservableProperty]
    private string _equalizerText = "--";

    [ObservableProperty]
    private bool? _gameModeEnabled;

    [ObservableProperty]
    private bool? _lowLatencyEnabled;

    [ObservableProperty]
    private bool? _dualConnectionEnabled;

    [ObservableProperty]
    private bool? _inEarDetectionEnabled;

    [ObservableProperty]
    private bool? _windSuppressionEnabled;

    [ObservableProperty]
    private bool _isApplyingControl;

    private bool _updatingAncSelection;

    public MainViewModel(
        HeadsetConnectionService connectionService,
        HeadsetControlService headsetControl,
        BatteryMonitorService batteryMonitor,
        NoiseCancellingService noiseCancelling,
        AutoProbeService autoProbe,
        IDeviceProtocol protocol,
        ProbeWindow probeWindow,
        SppProbeWindow sppProbeWindow,
        SppProbeViewModel sppProbeViewModel,
        VerifyWindow verifyWindow,
        VerifyViewModel verifyViewModel,
        ILogger<MainViewModel> logger)
    {
        _connectionService = connectionService;
        _headsetControl = headsetControl;
        _batteryMonitor = batteryMonitor;
        _noiseCancelling = noiseCancelling;
        _autoProbe = autoProbe;
        _protocol = protocol;
        _probeWindow = probeWindow;
        _sppProbeWindow = sppProbeWindow;
        _sppProbeViewModel = sppProbeViewModel;
        _verifyWindow = verifyWindow;
        _verifyViewModel = verifyViewModel;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current.Dispatcher;

        _subscriptions.Add(_connectionService.DevicesDiscovered
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnDeviceDiscovered));
        _subscriptions.Add(_connectionService.StateChanged
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnConnectionStateChanged));
        _subscriptions.Add(_batteryMonitor.BatteryChanged
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnBatteryChanged));
        _subscriptions.Add(_noiseCancelling.ModeChanged
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnModeChanged));
        _subscriptions.Add(_headsetControl.StateChanged
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnControlStateChanged));

        _connectionService.ControlSessionChanged += OnControlSessionChanged;
        _connectionService.SessionChanged += OnSessionChanged;

        _ancStatusText = "SPP 控制通道已就绪：连接后自动读取耳机状态。";
    }

    public bool IsPrivateProtocolAvailable => true;

    [RelayCommand]
    private async Task ToggleScanAsync()
    {
        if (IsScanning)
        {
            await _connectionService.StopScanAsync();
            IsScanning = false;
            StatusText = "扫描已停止";
            return;
        }

        Devices.Clear();
        IsScanning = true;
        StatusText = "正在扫描附近的蓝牙设备…";
        await _connectionService.StartScanAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        CanConnect = false;
        try
        {
            await _connectionService.ConnectAsync(SelectedDevice.Model, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接 {Name} 失败", SelectedDevice.Name);
            StatusText = $"连接失败：{ex.Message}";
        }
        finally
        {
            // 无论成败都恢复，保证连接成功后「配对/全自动探测」等按钮可用。
            CanConnect = true;
        }
    }

    /// <summary>与选中设备配对（未配对设备的特征写入会被 Windows 拒绝，配对后解锁）。</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task PairDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        CanConnect = false;
        try
        {
            StatusText = $"正在与 {SelectedDevice.Name} 配对…（如弹出 Windows 确认框请点确定）";
            var paired = await _connectionService.PairAsync(SelectedDevice.Model, CancellationToken.None);
            StatusText = paired
                ? $"已与 {SelectedDevice.Name} 配对，现在可以连接并写入。"
                : $"配对未完成（{SelectedDevice.Name}）。可尝试让耳机进入配对模式后重试。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "配对 {Name} 失败", SelectedDevice.Name);
            StatusText = $"配对失败：{ex.Message}";
        }
        finally
        {
            CanConnect = true;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _connectionService.DisconnectAsync();
        StatusText = "已断开";
    }

    /// <summary>打开协议探测窗口（已打开则激活前置）。</summary>
    [RelayCommand]
    private void OpenProbe()
    {
        if (_probeWindow.IsVisible)
        {
            if (_probeWindow.WindowState == WindowState.Minimized)
            {
                _probeWindow.WindowState = WindowState.Normal;
            }

            _probeWindow.Activate();
            return;
        }

        _probeWindow.Show();
    }

    /// <summary>打开 SPP/RFCOMM 串口探测窗口（已打开则激活前置）。</summary>
    [RelayCommand]
    private void OpenSppProbe()
    {
        if (_sppProbeWindow.IsVisible)
        {
            if (_sppProbeWindow.WindowState == WindowState.Minimized)
            {
                _sppProbeWindow.WindowState = WindowState.Normal;
            }

            _sppProbeWindow.Activate();
            return;
        }

        // 把主窗口当前选中设备的地址传给 SPP 窗口，打开时自动预选并枚举服务。
        _sppProbeViewModel.PreselectAddress = SelectedDevice?.Model.Address;
        _sppProbeWindow.Show();
    }

    /// <summary>打开协议自动校验窗口（已打开则激活前置），预选主窗口当前设备。</summary>
    [RelayCommand]
    private void OpenVerify()
    {
        if (_verifyWindow.IsVisible)
        {
            if (_verifyWindow.WindowState == WindowState.Minimized)
            {
                _verifyWindow.WindowState = WindowState.Normal;
            }

            _verifyWindow.Activate();
            return;
        }

        _verifyViewModel.PreselectAddress = SelectedDevice?.Model.Address;
        _verifyWindow.Show();
    }

    /// <summary>全自动协议试探：自动连接、枚举、订阅、写候选命令、捕获响应并导出报告。</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task RunAutoProbeAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        CanConnect = false;
        try
        {
            if (_connectionService.CurrentControlSession is null)
            {
                StatusText = "正在连接耳机…";
                await _connectionService.ConnectAsync(SelectedDevice.Model, CancellationToken.None);
            }

            StatusText = "全自动协议试探进行中（约 1 分钟）。请戴上耳机，留意降噪/通透是否随试探变化…";
            var report = await _autoProbe.RunAutoProbeAsync(CancellationToken.None);

            var probeDirectory = @"E:\Project\Bluetooth\probe";
            Directory.CreateDirectory(probeDirectory);
            var filePath = Path.Combine(probeDirectory, $"autoprobe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(filePath, report);
            StatusText = $"自动探测完成！报告：{filePath}";
            _logger.LogInformation("自动探测报告已导出：{Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全自动探测失败");
            StatusText = $"自动探测失败：{ex.Message}";
        }
        finally
        {
            CanConnect = true;
        }
    }

    partial void OnIsAncOffChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.Off);
    partial void OnIsAncOnChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.Deep);
    partial void OnIsTransparencyChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.Transparency);
    partial void OnIsAncNormalChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.Normal);
    partial void OnIsAncDeepChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.Deep);
    partial void OnIsAncExperimentalChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.Experimental);
    partial void OnIsAncWindSuppressionChanged(bool value) => RequestAncMode(value, NoiseCancellingMode.WindSuppression);

    private void RequestAncMode(bool selected, NoiseCancellingMode mode)
    {
        if (selected && !_updatingAncSelection)
        {
            _ = SwitchAncModeAsync(mode);
        }
    }

    private async Task SwitchAncModeAsync(NoiseCancellingMode mode)
    {
        if (IsApplyingControl)
        {
            return;
        }

        IsApplyingControl = true;
        try
        {
            await _noiseCancelling.SetModeAsync(mode, CancellationToken.None);
            StatusText = $"已确认切换：{DescribeMode(mode)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换降噪模式失败");
            StatusText = $"切换失败：{ex.Message}";
        }
        finally
        {
            IsApplyingControl = false;
        }
    }

    [RelayCommand]
    private async Task SetToggleAsync(string feature)
    {
        if (IsApplyingControl)
        {
            return;
        }

        var (toggle, current, label) = feature switch
        {
            "game" => (HeadsetToggleFeature.GameMode, GameModeEnabled, "游戏模式"),
            "latency" => (HeadsetToggleFeature.LowLatency, LowLatencyEnabled, "低延迟"),
            "dual" => (HeadsetToggleFeature.DualConnection, DualConnectionEnabled, "双设备连接"),
            "inear" => (HeadsetToggleFeature.InEarDetection, InEarDetectionEnabled, "入耳检测"),
            "wind" => (HeadsetToggleFeature.WindSuppression, WindSuppressionEnabled, "抗风噪"),
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };

        IsApplyingControl = true;
        try
        {
            await _headsetControl.SetToggleAsync(toggle, current != true, CancellationToken.None);
            StatusText = $"已确认切换：{label}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换 {Feature} 失败", label);
            StatusText = $"切换{label}失败：{ex.Message}";
        }
        finally
        {
            IsApplyingControl = false;
        }
    }

    [RelayCommand]
    private async Task SetCodecAsync(string codec)
    {
        if (!Enum.TryParse<HeadsetCodec>(codec, out var value) || IsApplyingControl)
        {
            return;
        }

        var confirm = MessageBox.Show(
            "编码切换属于实验功能：设备没有可靠的当前编码查询响应，切换可能造成断音或需要重新连接。是否继续发送指令？",
            "确认实验性编码切换",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsApplyingControl = true;
        try
        {
            await _headsetControl.SetCodecExperimentalAsync(value, CancellationToken.None);
            StatusText = $"已发送实验性编码指令：{value}；请留意音频是否重连。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换实验性编码失败");
            StatusText = $"切换编码失败：{ex.Message}";
        }
        finally
        {
            IsApplyingControl = false;
        }
    }

    [RelayCommand]
    private async Task SetEqualizerAsync(string preset)
    {
        if (!Enum.TryParse<EqualizerPreset>(preset, out var value) || value == EqualizerPreset.Unknown || IsApplyingControl)
        {
            return;
        }

        IsApplyingControl = true;
        try
        {
            await _headsetControl.SetEqualizerAsync(value, CancellationToken.None);
            StatusText = $"已确认切换 EQ：{value}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换 EQ 失败");
            StatusText = $"切换 EQ 失败：{ex.Message}";
        }
        finally
        {
            IsApplyingControl = false;
        }
    }

    private void OnDeviceDiscovered(HeadsetDevice device)
    {
        if (Devices.All(d => d.Model.DeviceId != device.DeviceId))
        {
            Devices.Add(HeadsetDeviceItemBuilder.Create(device));
        }
    }

    private void OnConnectionStateChanged(HeadsetConnectionState state)
    {
        IsConnected = state == HeadsetConnectionState.Connected;
        StatusText = state switch
        {
            HeadsetConnectionState.Connected => "已连接",
            HeadsetConnectionState.Connecting => "正在连接…",
            HeadsetConnectionState.Reconnecting => "连接丢失，正在自动重连…",
            _ => "未连接",
        };

        AncControlsEnabled = IsConnected && _noiseCancelling.IsAvailable;

        if (IsConnected)
        {
            DeviceName = SelectedDevice?.Name ?? _connectionService.CurrentControlSession?.Device.Name ?? "已连接设备";
        }
    }

    private void OnBatteryChanged(BatteryInfo info)
    {
        LeftBattery = FormatPercent(info.LeftEarPercent);
        RightBattery = FormatPercent(info.RightEarPercent);
        CaseBattery = FormatPercent(info.CasePercent);
    }

    private void OnModeChanged(NoiseCancellingMode mode)
    {
        _updatingAncSelection = true;
        try
        {
            IsAncOff = mode == NoiseCancellingMode.Off;
            IsAncOn = mode == NoiseCancellingMode.Deep;
            IsTransparency = mode == NoiseCancellingMode.Transparency;
            IsAncNormal = mode == NoiseCancellingMode.Normal;
            IsAncDeep = mode == NoiseCancellingMode.Deep;
            IsAncExperimental = mode == NoiseCancellingMode.Experimental;
            IsAncWindSuppression = mode == NoiseCancellingMode.WindSuppression;
        }
        finally
        {
            _updatingAncSelection = false;
        }
    }

    private void OnControlStateChanged(HeadsetControlState state)
    {
        FirmwareVersion = state.Firmware?.ToString() ?? "--";
        EqualizerText = state.Equalizer is { } preset ? DescribeEqualizer(preset) : "--";
        GameModeEnabled = state.GameModeEnabled;
        LowLatencyEnabled = state.LowLatencyEnabled;
        DualConnectionEnabled = state.DualConnectionEnabled;
        InEarDetectionEnabled = state.InEarDetectionEnabled;
        WindSuppressionEnabled = state.WindSuppressionEnabled;
    }

    /// <summary>GATT 仅用于诊断窗口，正式状态由 SPP 控制会话提供。</summary>
    private void OnSessionChanged(IGattDeviceSession? session)
    {
    }

    private void OnControlSessionChanged(ISppDeviceSession? session, long generation)
    {
        _ = Task.Run(async () =>
        {
            if (session is null)
            {
                await _headsetControl.DetachAsync();
                await _batteryMonitor.DetachAsync();
                await _noiseCancelling.DetachAsync();
                _dispatcher.Invoke(() =>
                {
                    LeftBattery = "--";
                    RightBattery = "--";
                    CaseBattery = "--";
                    DeviceName = "—";
                });
                return;
            }

            try
            {
                await _headsetControl.AttachAsync(session, generation, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化 SPP 控制会话失败");
                _dispatcher.Invoke(() => StatusText = $"读取耳机状态失败：{ex.Message}");
            }
        });
    }

    private static string FormatPercent(byte? value) => value is { } v ? $"{v}%" : "--";

    private static string DescribeMode(NoiseCancellingMode mode) => mode switch
    {
        NoiseCancellingMode.Off => "关闭",
        NoiseCancellingMode.Normal => "普通降噪",
        NoiseCancellingMode.Deep => "深度降噪",
        NoiseCancellingMode.Experimental => "试验性降噪",
        NoiseCancellingMode.WindSuppression => "风噪抑制",
        NoiseCancellingMode.Transparency => "通透",
        _ => "未知",
    };

    private static string DescribeEqualizer(EqualizerPreset preset) => preset switch
    {
        EqualizerPreset.Blue => "悔恨之泪",
        EqualizerPreset.Balanced => "均衡中正",
        EqualizerPreset.Bass => "欧美澎湃",
        EqualizerPreset.Pure => "真律还原",
        EqualizerPreset.Game => "游戏优化",
        EqualizerPreset.Fine => "细腻佳音",
        EqualizerPreset.Vocal => "温婉人声",
        _ => "未知模式",
    };

    /// <summary>观察模式：30 秒内记录状态寄存器与通知变化（用户手动操作耳机触发）。</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ObserveAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        CanConnect = false;
        try
        {
            if (_connectionService.CurrentControlSession is null)
            {
                StatusText = "正在连接耳机…";
                await _connectionService.ConnectAsync(SelectedDevice.Model, CancellationToken.None);
            }

            StatusText = "观察模式进行中（30 秒）。请戴上耳机，长按触控来回切换降噪/通透/关闭…";
            var report = await _autoProbe.ObserveStateAsync(CancellationToken.None);

            var probeDirectory = @"E:\Project\Bluetooth\probe";
            Directory.CreateDirectory(probeDirectory);
            var filePath = Path.Combine(probeDirectory, $"observe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(filePath, report);
            StatusText = $"观察完成！报告：{filePath}";
            _logger.LogInformation("状态观察报告已导出：{Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "观察模式失败");
            StatusText = $"观察失败：{ex.Message}";
        }
        finally
        {
            CanConnect = true;
        }
    }

    public void Dispose()
    {
        _connectionService.ControlSessionChanged -= OnControlSessionChanged;
        _connectionService.SessionChanged -= OnSessionChanged;
        _subscriptions.Dispose();
    }
}

/// <summary>设备项工厂（分离职责，便于测试）。</summary>
internal static class HeadsetDeviceItemBuilder
{
    public static HeadsetDeviceItem Create(HeadsetDevice device) => new() { Model = device };
}
