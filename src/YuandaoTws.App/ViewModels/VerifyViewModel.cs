using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YuandaoTws.Application.Services;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.App.ViewModels;

/// <summary>
/// 协议自动校验窗口 ViewModel：选择已配对设备 → 一键全自动校验（逐服务开流、
/// 发 NiceHCK/原道变体命令、双解析器判定格式）→ 结果表格 + 进度日志 + 报告落盘。
/// </summary>
public partial class VerifyViewModel : ObservableObject
{
    private readonly ProtocolVerifyService _verify;
    private readonly ILogger<VerifyViewModel> _logger;
    private const int MaxPhaseLog = 500;
    private const string ProbeDirectory = @"E:\Project\Bluetooth\probe";

    /// <summary>校验运行期间的实时日志写入器（从开始即落盘，成功/失败都保存完整过程）。</summary>
    private StreamWriter? _logWriter;

    /// <summary>当前日志文件路径（成功后即报告路径；失败时也保留供排查）。</summary>
    private string? _logPath;

    public ObservableCollection<HeadsetDeviceItem> Devices { get; } = [];

    /// <summary>各服务校验结果（表格展示，带格式中文名）。</summary>
    public ObservableCollection<VerifyServiceRowItem> Results { get; } = [];

    /// <summary>进度日志（倒序，最新在前）。</summary>
    public ObservableCollection<VerifyPhase> Phases { get; } = [];

    [ObservableProperty]
    private string _statusText = "选择已配对设备 → 开始自动校验（约 1-2 分钟）。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private HeadsetDeviceItem? _selectedDevice;

    /// <summary>窗口打开时希望预选中的设备地址（主窗口传入，按 MAC 匹配）。</summary>
    [ObservableProperty]
    private string? _preselectAddress;

    [ObservableProperty]
    private string? _reportPath;

    public VerifyViewModel(ProtocolVerifyService verify, ILogger<VerifyViewModel> logger)
    {
        _verify = verify;
        _logger = logger;
    }

    /// <summary>窗口每次变为可见时调用：自动枚举已配对设备并预选（PreselectAddress → 名称含 YUANDAO/OriG → 唯一）。</summary>
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
            var devices = await _verify.EnumerateDevicesAsync(CancellationToken.None);
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(HeadsetDeviceItemBuilder.Create(device));
            }

            SelectedDevice = Devices.FirstOrDefault(d =>
                    string.Equals(d.Model.Address, PreselectAddress, StringComparison.OrdinalIgnoreCase))
                ?? Devices.FirstOrDefault(d => IsTargetDevice(d.Model))
                ?? (Devices.Count == 1 ? Devices[0] : null);

            StatusText = SelectedDevice is not null
                ? $"已选中 {SelectedDevice.Name}。点「开始自动校验」；结果与日志在下方，报告自动落盘 probe 目录。"
                : devices.Count == 0
                    ? "未找到已配对设备。请先在 Windows 设置 → 蓝牙中与耳机配对（经典蓝牙音频配对即可）。"
                    : $"找到 {devices.Count} 个已配对设备，未识别出原道设备，请手动选择后开始校验。";
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
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (SelectedDevice is not null)
        {
            PreselectAddress = SelectedDevice.Model.Address;
        }

        await InitializeAsync();
    }

    /// <summary>一键全自动校验：枚举服务 → 逐服务开流/发命令/分析。过程日志实时落盘，成功失败都保存。</summary>
    [RelayCommand]
    private async Task RunVerifyAsync()
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
        Results.Clear();
        Phases.Clear();
        ReportPath = null;
        try
        {
            Directory.CreateDirectory(ProbeDirectory);
            _logPath = Path.Combine(ProbeDirectory, $"verify-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            _logWriter = new StreamWriter(_logPath, append: false, Encoding.UTF8) { AutoFlush = true };
            _logWriter.WriteLine("原道「原点」协议自动校验日志");
            _logWriter.WriteLine($"开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logWriter.WriteLine($"设备：{SelectedDevice.Name}（{SelectedDevice.Model.Address}）");
            _logWriter.WriteLine("格式：[时间] 方向 阶段：内容；RECV 明细逐块带到达时间。");
            _logWriter.WriteLine();

            StatusText = "自动校验进行中（约 1-2 分钟，候选服务逐个开流并发送两组命令）…";
            var progress = new Progress<VerifyPhase>(AddPhase);
            var report = await _verify.RunAsync(SelectedDevice.Model, progress, CancellationToken.None);

            foreach (var result in report.Services)
            {
                Results.Add(new VerifyServiceRowItem(result));
            }

            ReportPath = _logPath;
            await _logWriter.WriteAsync(BuildReportText(report));
            StatusText = $"校验完成（{report.Duration.TotalSeconds:0}s）！完整日志：{ReportPath}";
            _logger.LogInformation("协议校验完成，日志已保存：{Path}", ReportPath);
        }
        catch (Exception ex)
        {
            // 失败也保留完整日志（含异常堆栈），便于排查。
            _logger.LogError(ex, "协议自动校验失败");
            if (_logWriter is not null)
            {
                await _logWriter.WriteLineAsync();
                await _logWriter.WriteLineAsync("===== 校验异常 =====");
                await _logWriter.WriteLineAsync(ex.ToString());
            }

            ReportPath = _logPath;
            StatusText = $"校验失败：{ex.Message}（日志已保存：{ReportPath}）";
        }
        finally
        {
            _logWriter?.Dispose();
            _logWriter = null;
            IsBusy = false;
        }
    }

    /// <summary>把当前报告另存一份（校验完成已自动保存，此按钮兜底）。</summary>
    [RelayCommand]
    private void SaveReport()
    {
        if (ReportPath is null)
        {
            StatusText = "尚无报告可保存：请先完成一次自动校验。";
            return;
        }

        try
        {
            Directory.CreateDirectory(ProbeDirectory);
            var copyPath = Path.Combine(ProbeDirectory, $"verify-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.Copy(ReportPath, copyPath, overwrite: false);
            ReportPath = copyPath;
            StatusText = $"报告已另存为：{copyPath}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "另存校验报告失败");
            StatusText = $"另存失败：{ex.Message}";
        }
    }

    private void AddPhase(VerifyPhase phase)
    {
        Phases.Insert(0, phase);
        while (Phases.Count > MaxPhaseLog)
        {
            Phases.RemoveAt(Phases.Count - 1);
        }

        // 实时同步写入日志文件（AutoFlush，失败时也能拿到完整过程）。
        _logWriter?.WriteLine($"[{phase.Time}] {phase.Direction,-5} {phase.Stage}：{phase.Text}");
    }

    private static bool IsTargetDevice(HeadsetDevice device) =>
        device.Name.Contains("YUANDAO", StringComparison.OrdinalIgnoreCase)
        || device.Name.Contains("OriG", StringComparison.OrdinalIgnoreCase);

    /// <summary>生成汇总文本（每服务一行 + 结论），追加到实时日志文件末尾；阶段明细已在日志中。</summary>
    private static string BuildReportText(VerifyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"===== 汇总（耗时 {report.Duration.TotalSeconds:0.0}s）=====");
        foreach (var result in report.Services)
        {
            sb.AppendLine($"{result.ServiceName}（{result.ServiceId}）：开流 {result.OpenResult}，"
                + $"NiceHCK {result.NiceHckFrameCount} 帧 / 原道 {result.YuandaoFrameCount} 帧 → {FormatName(result.Format)}");
        }

        sb.AppendLine();
        sb.AppendLine("===== 结论 =====");
        sb.AppendLine(report.Conclusion);
        return sb.ToString();
    }

    private static string FormatName(ProtocolFormatGuess format) => format switch
    {
        ProtocolFormatGuess.NiceHck => "NiceHCK 格式（4E 头）",
        ProtocolFormatGuess.YuandaoVariant => "原道变体（03 头）",
        ProtocolFormatGuess.NoResponse => "无响应",
        _ => "未知",
    };
}

/// <summary>校验结果表格行（包装 <see cref="VerifyServiceResult"/> 并附格式中文名）。</summary>
public sealed class VerifyServiceRowItem
{
    public VerifyServiceRowItem(VerifyServiceResult result)
    {
        Result = result;
    }

    public VerifyServiceResult Result { get; }

    public string ServiceName => Result.ServiceName;

    public Guid ServiceId => Result.ServiceId;

    public string OpenResult => Result.OpenResult;

    public int NiceHckFrameCount => Result.NiceHckFrameCount;

    public int YuandaoFrameCount => Result.YuandaoFrameCount;

    public string FormatText => Result.Format switch
    {
        ProtocolFormatGuess.NiceHck => "✅ NiceHCK 格式",
        ProtocolFormatGuess.YuandaoVariant => "✅ 原道变体",
        ProtocolFormatGuess.NoResponse => "无响应",
        _ => "未知",
    };
}
