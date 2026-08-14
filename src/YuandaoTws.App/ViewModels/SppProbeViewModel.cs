using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YuandaoTws.Application.Services;
using YuandaoTws.Domain;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.App.ViewModels;

/// <summary>
/// SPP/RFCOMM 串口探测窗口 ViewModel：枚举已配对经典蓝牙设备、枚举 RFCOMM 服务、
/// 打开/关闭双向字节流、发送 hex、接收流数据 + 会话记录。
/// 与 GATT 探测窗口相互独立，不依赖 BLE 连接。
/// </summary>
public partial class SppProbeViewModel : ObservableObject, IDisposable
{
    private readonly SppProbeService _spp;
    private readonly ILogger<SppProbeViewModel> _logger;
    private IDisposable? _dataSubscription;
    private const int MaxStreamLog = 500;
    private const string ProbeDirectory = @"E:\Project\Bluetooth\probe";

    // 会话记录：WRITE/RECV/标记全部按时间追加到 probe\spp-session-*.txt。
    private readonly object _sessionLock = new();
    private StreamWriter? _sessionWriter;
    private string? _sessionLogPath;

    public ObservableCollection<HeadsetDeviceItem> Devices { get; } = [];

    public ObservableCollection<RfcommServiceItem> Services { get; } = [];

    public ObservableCollection<SppLogItem> StreamLog { get; } = [];

    [ObservableProperty]
    private string _statusText = "选择已配对设备 → 枚举服务 → 打开 SPP 流 → 发送试探。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private HeadsetDeviceItem? _selectedDevice;

    /// <summary>窗口打开时希望预选中的设备地址（主窗口传入，按 MAC 匹配）。</summary>
    [ObservableProperty]
    private string? _preselectAddress;

    [ObservableProperty]
    private RfcommServiceItem? _selectedService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _writeHex = "01";

    [ObservableProperty]
    private string _markerText = "切到通透";

    [ObservableProperty]
    private string _sessionLogState = "会话记录：未开启";

    public SppProbeViewModel(SppProbeService spp, ILogger<SppProbeViewModel> logger)
    {
        _spp = spp;
        _logger = logger;
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        // 保留当前选中的设备地址，重新枚举已配对设备。
        if (SelectedDevice is not null)
        {
            PreselectAddress = SelectedDevice.Model.Address;
        }

        await InitializeAsync();
    }

    /// <summary>
    /// 窗口每次变为可见时调用：自动枚举已配对设备 → 按 <see cref="PreselectAddress"/> 预选
    /// （无则仅剩 1 台时选中它）→ 自动枚举服务并选中串口/唯一服务。
    /// 之后用户只需点「打开 SPP 流」。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在枚举已配对经典蓝牙设备…";
        try
        {
            var devices = await _spp.EnumeratePairedDevicesAsync(CancellationToken.None);
            Devices.Clear();
            HeadsetDeviceItem? preselect = null;
            foreach (var device in devices)
            {
                var item = HeadsetDeviceItemBuilder.Create(device);
                Devices.Add(item);
                if (!string.IsNullOrEmpty(PreselectAddress)
                    && string.Equals(item.Model.Address, PreselectAddress, StringComparison.OrdinalIgnoreCase))
                {
                    preselect = item;
                }
            }

            // 预选：优先 PreselectAddress 匹配项；未指定且只有一台时选它。
            if (preselect is not null)
            {
                SelectedDevice = preselect;
                StatusText = $"已自动选中 {preselect.Name}，正在枚举服务…";
            }
            else if (Devices.Count == 1)
            {
                SelectedDevice = Devices[0];
                StatusText = $"已自动选中 {Devices[0].Name}，正在枚举服务…";
            }
            else
            {
                StatusText = devices.Count == 0
                    ? "未找到已配对设备。请先在 Windows 设置 → 蓝牙中与耳机配对（经典蓝牙音频配对即可）。"
                    : $"找到 {devices.Count} 个已配对设备，请从下拉中选择。";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "枚举已配对经典蓝牙设备失败");
            StatusText = $"枚举失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // 选中设备后自动枚举服务（选中动作发生在 IsBusy 期间，需在此显式触发）。
        if (SelectedDevice is not null)
        {
            await EnumerateServicesAsync();
        }
    }

    partial void OnSelectedDeviceChanged(HeadsetDeviceItem? value)
    {
        // 手动换设备时自动枚举其 RFCOMM 服务。
        if (value is not null && !IsBusy)
        {
            _ = EnumerateServicesAsync();
        }
    }

    [RelayCommand]
    private async Task EnumerateServicesAsync()
    {
        if (SelectedDevice is null)
        {
            StatusText = "请先选择一个设备。";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在枚举 RFCOMM 服务…";
        try
        {
            var services = await _spp.EnumerateServicesAsync(SelectedDevice.Model, CancellationToken.None);
            Services.Clear();
            foreach (var service in services)
            {
                Services.Add(new RfcommServiceItem
                {
                    ServiceId = service.ServiceId,
                    ServiceName = service.ServiceName,
                    ChannelName = service.ChannelName ?? string.Empty,
                });
            }

            // 自动选中：优先串口服务(0x1101)；无串口且仅一个服务则选它；否则留空提示手动挑。
            if (Services.Count > 0)
            {
                var serial = Services.FirstOrDefault(s => s.ServiceId == RfcommServiceNames.SerialPort);
                if (serial is not null)
                {
                    SelectedService = serial;
                    StatusText = $"枚举完成：{services.Count} 个服务，已自动选中串口「{serial.ServiceName}」。点「打开 SPP 流」。";
                }
                else if (Services.Count == 1)
                {
                    SelectedService = Services[0];
                    StatusText = $"枚举完成：仅 {services.Count} 个服务「{Services[0].ServiceName}」，已自动选中。点「打开 SPP 流」。";
                }
                else
                {
                    SelectedService = null;
                    StatusText = $"枚举完成：{services.Count} 个服务，未发现串口(0x1101)。请手动挑选可能是控制通道的服务（厂商私有 UUID）后点「打开 SPP 流」。";
                }
            }
            else
            {
                SelectedService = null;
                StatusText = $"「{SelectedDevice.Name}」未发现 RFCOMM 服务。";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "枚举 {Device} 的 RFCOMM 服务失败", SelectedDevice.Name);
            StatusText = $"枚举失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 一键复制全部已枚举服务的名称、UUID、通道（供人工分析该选哪个）。
    /// 剪贴板可能被系统占用导致阻塞/失败，故同时写入 probe 目录文件兜底，
    /// 剪贴板在后台 STA 线程设置，避免卡死 UI。
    /// </summary>
    [RelayCommand]
    private void CopyServices()
    {
        if (Services.Count == 0)
        {
            StatusText = "请先枚举服务。";
            return;
        }

        try
        {
            Directory.CreateDirectory(ProbeDirectory);
            var filePath = Path.Combine(ProbeDirectory, $"spp-services-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(filePath, BuildServicesText(), Encoding.UTF8);

            CopyToClipboardInBackground(BuildServicesText());

            StatusText = $"已复制 {Services.Count} 个服务；同时保存到 {filePath}。若粘贴无内容，请直接打开该文件。";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复制服务列表失败");
            StatusText = $"复制失败：{ex.Message}";
        }
    }

    private string BuildServicesText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RFCOMM 服务列表：{SelectedDevice?.Name ?? "（设备未知）"}（{SelectedDevice?.Model.Address ?? "-"}）");
        sb.AppendLine($"共 {Services.Count} 个服务：");
        for (var i = 0; i < Services.Count; i++)
        {
            var service = Services[i];
            sb.AppendLine($"[{i + 1}] {service.ServiceName} | {service.ServiceId} | 通道: {service.ChannelName}");
        }

        return sb.ToString();
    }

    /// <summary>在后台 STA 线程设置剪贴板文本，避免剪贴板被占用时阻塞 UI 线程。失败静默，文件已兜底。</summary>
    private static void CopyToClipboardInBackground(string text)
    {
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                // 剪贴板被占用/不可用：静默失败，文件已兜底。
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [RelayCommand]
    private async Task OpenStreamAsync()
    {
        if (SelectedDevice is null)
        {
            StatusText = "请先选择设备。";
            return;
        }

        if (SelectedService is null)
        {
            StatusText = "请先在服务列表选中一个服务。";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在打开 SPP 流…";
        try
        {
            await _spp.OpenAsync(SelectedDevice.Model, SelectedService.ServiceId, CancellationToken.None);
            _dataSubscription?.Dispose();
            _dataSubscription = _spp.DataReceived!
                .ObserveOn(DispatcherScheduler.Current)
                .Subscribe(OnDataReceived);
            IsConnected = true;
            StatusText = $"SPP 流已打开：{SelectedService.ServiceName}。可发送 hex 试探，回包会出现在下方流日志。";
            LogSession($"OPEN {SelectedService.ServiceName}（{SelectedService.ServiceId}）");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开 SPP 流失败");
            StatusText = $"打开失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CloseStreamAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _dataSubscription?.Dispose();
            _dataSubscription = null;
            await _spp.CloseAsync();
            IsConnected = false;
            StatusText = "SPP 流已关闭。";
            LogSession("== CLOSE ==");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭 SPP 流失败");
            StatusText = $"关闭失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>把输入框里的 hex 发送到打开的 SPP 流。</summary>
    [RelayCommand]
    private Task SendAsync() => SendHexAsync(WriteHex ?? string.Empty);

    /// <summary>快捷发送：预设 hex 一键发出。</summary>
    [RelayCommand]
    private Task QuickSendAsync(string presetHex) => SendHexAsync(presetHex);

    private async Task SendHexAsync(string hexInput)
    {
        if (!IsConnected)
        {
            StatusText = "请先打开 SPP 流。";
            return;
        }

        if (IsBusy)
        {
            return;
        }

        var hex = hexInput.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            StatusText = "请输入正确的十六进制，如：01 或 FE 01";
            return;
        }

        byte[] data;
        try
        {
            data = Convert.FromHexString(hex);
        }
        catch
        {
            StatusText = "输入的 hex 格式不正确。";
            return;
        }

        IsBusy = true;
        try
        {
            await _spp.WriteAsync(data, CancellationToken.None);
            var line = FormatCompact(data);
            AddLog("WRITE", line);
            LogSession($"WRITE {line}");
            StatusText = $"已发送：{line}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SPP 发送失败");
            StatusText = $"发送失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>开关会话记录：之后的所有发送/接收/标记按时间追加到 probe\spp-session-*.txt。</summary>
    [RelayCommand]
    private void ToggleSessionLog()
    {
        lock (_sessionLock)
        {
            if (_sessionWriter is not null)
            {
                LogSessionLocked("== 记录结束 ==");
                _sessionWriter.Dispose();
                _sessionWriter = null;
                SessionLogState = $"已保存：{_sessionLogPath}";
                StatusText = $"会话记录已保存：{_sessionLogPath}";
                return;
            }

            Directory.CreateDirectory(ProbeDirectory);
            _sessionLogPath = Path.Combine(ProbeDirectory, $"spp-session-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            _sessionWriter = new StreamWriter(_sessionLogPath, append: false, Encoding.UTF8) { AutoFlush = true };
            _sessionWriter.WriteLine($"原道「原点」SPP 会话记录  开始于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _sessionWriter.WriteLine($"设备：{SelectedDevice?.Name ?? "（未选）"}    地址：{SelectedDevice?.Model.Address ?? "-"}");
            _sessionWriter.WriteLine("记录 WRITE（发送）/ RECV（接收）/ 标记 事件，供逆向分析。");
            SessionLogState = $"记录中：{Path.GetFileName(_sessionLogPath)}";
            StatusText = "会话记录已开启：发送/接收/标记都会记入文件，操作完点「开始/停止记录」结束。";
        }
    }

    /// <summary>在会话记录中插入一条人工标记（如「切到通透」「放进充电盒」）。</summary>
    [RelayCommand]
    private void AddMarker()
    {
        var marker = (MarkerText ?? string.Empty).Trim();
        if (marker.Length == 0)
        {
            StatusText = "请先输入标记内容。";
            return;
        }

        LogSession($"== {marker} ==");
        StatusText = $"已标记：{marker}";
    }

    private void OnDataReceived(SppDataReceived data)
    {
        var line = FormatCompact(data.Value);
        AddLog("RECV", line);
        LogSession($"RECV {line}");
    }

    private void AddLog(string direction, string hex)
    {
        StreamLog.Insert(0, new SppLogItem
        {
            Time = DateTime.Now.ToString("HH:mm:ss.fff"),
            Direction = direction,
            Hex = hex,
        });
        while (StreamLog.Count > MaxStreamLog)
        {
            StreamLog.RemoveAt(StreamLog.Count - 1);
        }
    }

    private void LogSession(string line)
    {
        lock (_sessionLock)
        {
            LogSessionLocked(line);
        }
    }

    private void LogSessionLocked(string line)
    {
        if (_sessionWriter is null)
        {
            return;
        }

        _sessionWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
    }

    private static string FormatCompact(byte[] data) =>
        data.Length == 0 ? "（空）" : string.Join(" ", data.Select(b => b.ToString("X2")));

    /// <summary>窗口关闭时调用：关闭 SPP 流、断开订阅、刷掉会话记录。</summary>
    public async Task CleanupAsync()
    {
        _dataSubscription?.Dispose();
        _dataSubscription = null;
        IsConnected = false;
        try
        {
            await _spp.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭 SPP 流失败（窗口关闭清理）");
        }

        lock (_sessionLock)
        {
            _sessionWriter?.Dispose();
            _sessionWriter = null;
        }
    }

    public void Dispose()
    {
        _dataSubscription?.Dispose();
        lock (_sessionLock)
        {
            _sessionWriter?.Dispose();
            _sessionWriter = null;
        }
    }
}

/// <summary>RFCOMM 服务列表中的一行。</summary>
public sealed class RfcommServiceItem
{
    public required Guid ServiceId { get; init; }

    public required string ServiceName { get; init; }

    public required string ChannelName { get; init; }
}

/// <summary>SPP 流日志条目（方向 + hex）。</summary>
public sealed class SppLogItem
{
    public required string Time { get; init; }

    public required string Direction { get; init; }

    public required string Hex { get; init; }
}
