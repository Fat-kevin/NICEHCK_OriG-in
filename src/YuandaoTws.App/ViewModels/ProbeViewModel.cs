using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
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
/// 协议探测窗口 ViewModel：枚举设备全部 GATT 通道、读取特征值、监听通知、导出报告。
/// 这是协议逆向的第一步（对应设计文档 FR-10「协议探测模式」），用户只需连接耳机后点几下。
/// </summary>
public partial class ProbeViewModel : ObservableObject, IDisposable
{
    private readonly ProtocolProbeService _probe;
    private readonly ILogger<ProbeViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, byte[]> _readValues = new();
    private readonly Dictionary<Guid, byte[]> _notifications = new();
    private IDisposable? _notificationSubscription;
    private const int MaxNotificationLog = 500;
    private const string ProbeDirectory = @"E:\Project\Bluetooth\probe";

    // 会话记录：读/写/通知/标记全部按时间追加到 probe\session-*.txt。
    private readonly object _sessionLock = new();
    private StreamWriter? _sessionWriter;
    private string? _sessionLogPath;

    public ObservableCollection<GattServiceItem> Services { get; } = [];

    public ObservableCollection<GattNotificationItem> NotificationLog { get; } = [];

    [ObservableProperty]
    private string _statusText = "连接耳机后，点击「开始探测」枚举全部 GATT 通道。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _reportPath = "";

    [ObservableProperty]
    private GattServiceItem? _selectedCharacteristic;

    [ObservableProperty]
    private string _writeHex = "01";

    [ObservableProperty]
    private bool _writeWithoutResponse;

    [ObservableProperty]
    private string _markerText = "切到通透";

    [ObservableProperty]
    private string _sessionLogState = "会话记录：未开启";

    public ProbeViewModel(ProtocolProbeService probe, ILogger<ProbeViewModel> logger)
    {
        _probe = probe;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current.Dispatcher;
    }

    [RelayCommand]
    private async Task EnumerateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (_probe.CurrentSession is null)
        {
            StatusText = "尚未连接耳机。请先在主窗口扫描并连接。";
            return;
        }

        IsBusy = true;
        StatusText = "正在枚举全部 GATT 服务与特征…";
        try
        {
            var services = await _probe.EnumerateAsync(CancellationToken.None);
            _readValues.Clear();
            _notifications.Clear();
            NotificationLog.Clear();
            Services.Clear();
            foreach (var service in services)
            {
                foreach (var characteristic in service.Characteristics)
                {
                    Services.Add(new GattServiceItem
                    {
                        ServiceName = GattReportFormatter.DescribeUuid(service.Uuid),
                        ServiceUuid = service.Uuid,
                        CharacteristicUuid = characteristic.Uuid,
                        CharacteristicName = GattReportFormatter.DescribeUuid(characteristic.Uuid),
                        Properties = characteristic.Properties,
                        PropertiesText = GattReportFormatter.DescribeProperties(characteristic.Properties),
                    });
                }
            }

            StatusText = $"枚举完成：{services.Count} 个服务、{Services.Count} 个特征。可「读取全部」或「监听通知」。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "协议探测枚举失败");
            StatusText = $"枚举失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReadAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (Services.Count == 0)
        {
            StatusText = "请先「开始探测」枚举特征。";
            return;
        }

        IsBusy = true;
        var readable = Services.Where(s => s.Properties.HasFlag(GattCharacteristicProperties.Read)).ToArray();
        StatusText = $"正在读取 {readable.Length} 个可读特征…（部分特征只支持通知、不支持主动读，属正常）";
        var read = 0;
        foreach (var item in readable)
        {
            try
            {
                var value = await _probe.ReadAsync(item.CharacteristicUuid, CancellationToken.None);
                if (value is not null)
                {
                    _readValues[item.CharacteristicUuid] = value;
                    item.ValueText = GattReportFormatter.FormatHex(value);
                    read++;
                    LogSession($"READ {item.CharacteristicUuid}: {GattReportFormatter.FormatHex(value)}");
                }
                else
                {
                    item.ValueText = "（无响应）";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取特征 {Uuid} 失败", item.CharacteristicUuid);
                item.ValueText = $"（读取失败）";
            }
        }

        StatusText = $"读取完成：{read}/{readable.Length} 个特征返回了值。";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ListenAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (Services.Count == 0)
        {
            StatusText = "请先「开始探测」枚举特征。";
            return;
        }

        var session = _probe.CurrentSession;
        if (session is null)
        {
            StatusText = "连接已断开，请重新连接。";
            return;
        }

        IsBusy = true;
        var notifyCharacteristics = Services
            .Where(s => s.Properties.HasFlag(GattCharacteristicProperties.Notify)
                     || s.Properties.HasFlag(GattCharacteristicProperties.Indicate))
            .ToArray();
        StatusText = $"正在订阅 {notifyCharacteristics.Length} 个可通知特征…";
        var subscribed = 0;
        foreach (var item in notifyCharacteristics)
        {
            try
            {
                await _probe.SubscribeAsync(item.CharacteristicUuid, CancellationToken.None);
                subscribed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "订阅特征 {Uuid} 失败", item.CharacteristicUuid);
            }
        }

        _notificationSubscription?.Dispose();
        _notificationSubscription = session.Notifications
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnNotification);
        StatusText = $"已订阅 {subscribed}/{notifyCharacteristics.Length} 个特征。请在耳机或手机 APP 上操作（切换降噪、查看电量），观察下方哪个特征在推送数据。";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (Services.Count == 0)
        {
            StatusText = "请先「开始探测」。";
            return;
        }

        var session = _probe.CurrentSession;
        if (session is null)
        {
            StatusText = "连接已断开，无法导出。";
            return;
        }

        IsBusy = true;
        try
        {
            var services = await _probe.EnumerateAsync(CancellationToken.None);
            var report = GattReportFormatter.BuildReport(session.Device, services, _readValues, _notifications);

            // 报告导出到项目根下的 probe 目录（用户要求不放系统盘）。
            var probeDirectory = @"E:\Project\Bluetooth\probe";
            Directory.CreateDirectory(probeDirectory);
            var filePath = Path.Combine(
                probeDirectory,
                $"probe-{session.Device.Address.Replace(':', '_')}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(filePath, report);
            ReportPath = filePath;
            StatusText = $"报告已导出：{filePath}";
            _logger.LogInformation("协议探测报告已导出：{Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出协议探测报告失败");
            StatusText = $"导出失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>向选中的特征写入试探字节（协议盲试：写完留意通知日志与耳机物理反应）。</summary>
    [RelayCommand]
    private Task WriteCharacteristicAsync() => WriteHexToSelectedAsync(WriteHex ?? string.Empty);

    /// <summary>快速写入：预设 hex 一键写入选中的特征（配合「无响应写」开关）。</summary>
    [RelayCommand]
    private Task QuickWriteAsync(string presetHex) => WriteHexToSelectedAsync(presetHex);

    private async Task WriteHexToSelectedAsync(string hexInput)
    {
        if (IsBusy)
        {
            return;
        }

        var selected = SelectedCharacteristic;
        if (selected is null)
        {
            StatusText = "请先在表格中选中一个特征。";
            return;
        }

        if (_probe.CurrentSession is null)
        {
            StatusText = "未连接设备。";
            return;
        }

        var hex = hexInput.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            StatusText = "请输入正确的十六进制，如：01 或 01 02 0A";
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
            var mode = WriteWithoutResponse ? "无响应" : "带响应";
            await _probe.WriteAsync(selected.CharacteristicUuid, data, CancellationToken.None, !WriteWithoutResponse);
            StatusText = $"已写入 {selected.CharacteristicName}（{mode}）：{Convert.ToHexString(data)}。若该特征可通知，响应会出现在下方通知日志。";
            _logger.LogInformation("协议试探写入 {Uuid}（{Mode}）：{Hex}", selected.CharacteristicUuid, mode, Convert.ToHexString(data));
            LogSession($"WRITE {selected.CharacteristicUuid}（{mode}）: {FormatCompact(data)}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入 {Uuid} 失败", selected.CharacteristicUuid);
            StatusText = $"写入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>读取选中的特征值（手动对照测试：读一次 → 耳机操作 → 再读一次）。</summary>
    [RelayCommand]
    private async Task ReadSelectedAsync()
    {
        var selected = SelectedCharacteristic;
        if (selected is null)
        {
            StatusText = "请先在表格中选中一个特征。";
            return;
        }

        if (_probe.CurrentSession is null)
        {
            StatusText = "未连接设备。";
            return;
        }

        try
        {
            var value = await _probe.ReadAsync(selected.CharacteristicUuid, CancellationToken.None);
            if (value is null)
            {
                selected.ValueText = "（无响应）";
                StatusText = $"{selected.CharacteristicName} 无响应（可能不支持主动读）。";
                LogSession($"READ {selected.CharacteristicUuid}: （无响应）");
            }
            else
            {
                var hex = GattReportFormatter.FormatHex(value);
                selected.ValueText = hex;
                StatusText = $"{selected.CharacteristicName} = {hex}";
                LogSession($"READ {selected.CharacteristicUuid}: {hex}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 {Uuid} 失败", selected.CharacteristicUuid);
            StatusText = $"读取失败：{ex.Message}";
        }
    }

    /// <summary>一键复制全部特征的读值（含 UUID 与属性）到剪贴板。</summary>
    [RelayCommand]
    private void CopyValues()
    {
        if (Services.Count == 0)
        {
            StatusText = "请先「开始探测」。";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("服务\t特征\t属性\t读值\t最近通知");
        foreach (var item in Services)
        {
            sb.AppendLine($"{item.ServiceName}\t{item.CharacteristicName}\t{item.PropertiesText}\t{item.ValueText ?? "（未读）"}\t{item.LastNotification ?? string.Empty}");
        }

        Clipboard.SetText(sb.ToString());
        StatusText = $"已复制 {Services.Count} 行特征值到剪贴板。";
    }

    /// <summary>复制选中行的 UUID 与值。</summary>
    [RelayCommand]
    private void CopySelected()
    {
        var selected = SelectedCharacteristic;
        if (selected is null)
        {
            StatusText = "请先选中一个特征。";
            return;
        }

        Clipboard.SetText($"{selected.CharacteristicUuid}\t{selected.ValueText ?? "（未读）"}");
        StatusText = "已复制选中特征的 UUID 与读值。";
    }

    /// <summary>开关会话记录：之后的所有读取/写入/通知/标记按时间追加到 probe\session-*.txt。</summary>
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

            var session = _probe.CurrentSession;
            Directory.CreateDirectory(ProbeDirectory);
            _sessionLogPath = Path.Combine(ProbeDirectory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            _sessionWriter = new StreamWriter(_sessionLogPath, append: false, Encoding.UTF8) { AutoFlush = true };
            _sessionWriter.WriteLine($"原道「原点」会话记录  开始于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _sessionWriter.WriteLine($"设备：{session?.Device.Name ?? "（未连接）"}    地址：{session?.Device.Address ?? "-"}");
            _sessionWriter.WriteLine("记录 READ（读特征）/ WRITE（写特征）/ NOTIFY（通知）/ 标记 事件，供逆向分析。");
            SessionLogState = $"记录中：{Path.GetFileName(_sessionLogPath)}";
            StatusText = "会话记录已开启：读取/写入/通知/标记都会记入文件，操作完点「开始/停止记录」结束。";
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

    private void OnNotification(GattNotification notification)
    {
        _notifications[notification.CharacteristicUuid] = notification.Value;
        var item = Services.FirstOrDefault(s => s.CharacteristicUuid == notification.CharacteristicUuid);
        if (item is not null)
        {
            item.LastNotification = GattReportFormatter.FormatHex(notification.Value);
        }

        var entry = new GattNotificationItem
        {
            Time = DateTime.Now.ToString("HH:mm:ss.fff"),
            Uuid = notification.CharacteristicUuid.ToString(),
            Hex = GattReportFormatter.FormatHex(notification.Value),
        };
        NotificationLog.Insert(0, entry);
        while (NotificationLog.Count > MaxNotificationLog)
        {
            NotificationLog.RemoveAt(NotificationLog.Count - 1);
        }

        LogSession($"NOTIFY {notification.CharacteristicUuid}: {GattReportFormatter.FormatHex(notification.Value)}");
    }

    public void Dispose()
    {
        _notificationSubscription?.Dispose();
        lock (_sessionLock)
        {
            _sessionWriter?.Dispose();
            _sessionWriter = null;
        }
    }
}

/// <summary>探测结果中的一行特征（属性变更自动通知 DataGrid 刷新）。</summary>
public partial class GattServiceItem : ObservableObject
{
    public required string ServiceName { get; init; }

    public required Guid ServiceUuid { get; init; }

    public required Guid CharacteristicUuid { get; init; }

    public required string CharacteristicName { get; init; }

    public required GattCharacteristicProperties Properties { get; init; }

    public required string PropertiesText { get; init; }

    [ObservableProperty]
    private string? _valueText;

    [ObservableProperty]
    private string? _lastNotification;
}

/// <summary>通知日志条目。</summary>
public sealed class GattNotificationItem
{
    public required string Time { get; init; }

    public required string Uuid { get; init; }

    public required string Hex { get; init; }
}
