using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using YuandaoTws.Domain;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>
/// 协议自动校验编排：对目标设备的每个候选 RFCOMM 服务自动开流，
/// 依次发送 NiceHCK 格式与「原道变体」格式的查询命令，双解析器分析响应，
/// 判定哪个服务是控制通道、走哪种协议格式。全自动，产出一份 <see cref="VerifyReport"/>。
/// 单个服务内部异常不会中断整体校验（记录后继续下一服务）；阶段记录带时间戳与耗时，
/// 供失败时精确排查。
/// </summary>
public sealed class ProtocolVerifyService
{
    private static readonly Guid NiceHckControlService = new("0000a100-1000-8000-4e48-434b4354524c");
    private static readonly Guid YuandaoSppCandidate = new("df21fe2c-2515-4fdb-8886-f12c4d67927c");
    private static readonly Guid BesMagic6666 = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid BesMagic9999 = new("99999999-9999-9999-9999-999999999999");
    private static readonly Guid HandsfreeService = new("0000111e-0000-1000-8000-00805f9b34fb");

    private static readonly TimeSpan PushWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NiceHckPacing = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan NiceHckWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan YuandaoPacing = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan YuandaoWait = TimeSpan.FromSeconds(1.5);
    private const int MaxHexPerLine = 512; // 单行 hex 截断长度（字节），防大块刷屏。

    /// <summary>NiceHCK 查询组（对照 docs/protocol/nicehck-bes-protocol.md §2.2）。</summary>
    private static readonly (byte[] Frame, string Label)[] NiceHckQueries =
    [
        (NiceHckCommands.QueryFirmware(), "查固件"),
        (NiceHckCommands.QueryBattery(), "查电量"),
        (NiceHckCommands.QueryAnc(), "查降噪"),
        (NiceHckCommands.QueryEq(), "查 EQ"),
        (NiceHckCommands.QueryGameMode(), "查游戏模式"),
        (NiceHckCommands.QueryLowLatency(), "查低延迟"),
    ];

    /// <summary>原道变体查询组（格式推测：03 &lt;id&gt; 00 00；id=03 疑似电量）。</summary>
    private static readonly (byte[] Frame, string Label)[] YuandaoQueries =
    [
        (YuandaoCommands.Query(0x03), "03 id=03 查电量（推测）"),
        (YuandaoCommands.Query(0x01), "03 id=01 盲探"),
        (YuandaoCommands.Query(0x02), "03 id=02 盲探"),
        (YuandaoCommands.Query(0x04), "03 id=04 盲探"),
        (YuandaoCommands.Query(0x05), "03 id=05 盲探"),
    ];

    private readonly IRfcommServiceEnumerator _enumerator;
    private readonly ISppConnectionFactory _factory;
    private readonly ILogger<ProtocolVerifyService> _logger;

    public ProtocolVerifyService(
        IRfcommServiceEnumerator enumerator,
        ISppConnectionFactory factory,
        ILogger<ProtocolVerifyService> logger)
    {
        _enumerator = enumerator;
        _factory = factory;
        _logger = logger;
    }

    /// <summary>枚举本机已配对的经典蓝牙设备（校验前置步骤）。</summary>
    public Task<IReadOnlyList<HeadsetDevice>> EnumerateDevicesAsync(CancellationToken cancellationToken)
        => _enumerator.EnumeratePairedDevicesAsync(cancellationToken);

    /// <summary>
    /// 对目标设备执行一轮全自动协议校验。逐服务开流 → 采集推帧 → 发两组查询 → 分析帧格式，
    /// 全程通过 <paramref name="progress"/> 推送阶段记录（SEND/RECV/INFO）。
    /// 枚举失败不抛异常：返回空服务列表的报告，结论为错误说明。
    /// </summary>
    public async Task<VerifyReport> RunAsync(
        HeadsetDevice device,
        IProgress<VerifyPhase>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.Now;
        IReadOnlyList<RfcommServiceInfo> services;
        try
        {
            services = await _enumerator.GetServicesAsync(device, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "枚举 {Device} 的 RFCOMM 服务失败", device.Name);
            Report(progress, "服务枚举", "INFO", $"枚举失败：{ex}");
            return new VerifyReport
            {
                DeviceName = device.Name,
                DeviceAddress = device.Address,
                StartedAt = startedAt,
                Duration = DateTime.Now - startedAt,
                Services = [],
                Conclusion = $"枚举 {device.Name} 的 RFCOMM 服务失败：{ex.Message}。"
                    + "请确认耳机已开机配对且在蓝牙范围内（可先播放音频唤醒）。",
            };
        }

        var candidates = services
            .Where(s => s.ServiceId != HandsfreeService)
            .OrderBy(Rank)
            .ToArray();
        Report(progress, "服务枚举", "INFO", $"共 {services.Count} 个 SDP 服务，跳过免提后 {candidates.Length} 个候选。");
        foreach (var service in candidates)
        {
            Report(progress, "服务枚举", "INFO",
                $"候选[{Rank(service)}] {service.ServiceName}（{service.ServiceId}）", null);
        }

        var results = new List<VerifyServiceResult>();
        foreach (var service in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProbeServiceAsync(device, service, progress, cancellationToken));
        }

        return new VerifyReport
        {
            DeviceName = device.Name,
            DeviceAddress = device.Address,
            StartedAt = startedAt,
            Duration = DateTime.Now - startedAt,
            Services = results,
            Conclusion = BuildConclusion(device, results),
        };
    }

    /// <summary>候选服务排序：0000a100（NiceHCK 控制服务）→ df21fe2c → 66666666 → 99999999 → 其余。</summary>
    private static int Rank(RfcommServiceInfo service) =>
        service.ServiceId == NiceHckControlService ? 0
        : service.ServiceId == YuandaoSppCandidate ? 1
        : service.ServiceId == BesMagic6666 ? 2
        : service.ServiceId == BesMagic9999 ? 3
        : 4;

    /// <summary>对单个服务执行完整校验；内部任何异常都记录后返回（不中断整体）。</summary>
    private async Task<VerifyServiceResult> ProbeServiceAsync(
        HeadsetDevice device,
        RfcommServiceInfo service,
        IProgress<VerifyPhase>? progress,
        CancellationToken cancellationToken)
    {
        var phases = new List<VerifyPhase>();
        Report(progress, service.ServiceName, "INFO", $"开始校验：{service.ServiceId}", phases);

        var sw = Stopwatch.StartNew();
        ISppDeviceSession? session;
        try
        {
            session = await _factory.OpenAsync(device, service.ServiceId, cancellationToken);
            Report(progress, service.ServiceName, "INFO", $"SPP 流打开成功（{sw.ElapsedMilliseconds}ms）。", phases);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开 {Service} SPP 流失败", service.ServiceId);
            Report(progress, service.ServiceName, "INFO",
                $"开流失败（{sw.ElapsedMilliseconds}ms）：{ex}", phases);
            return new VerifyServiceResult
            {
                ServiceId = service.ServiceId,
                ServiceName = service.ServiceName,
                OpenResult = ex.Message,
                Phases = phases,
                NiceHckFrameCount = 0,
                YuandaoFrameCount = 0,
                Format = ProtocolFormatGuess.NoResponse,
            };
        }

        var niceCount = 0;
        var yuandaoCount = 0;
        var collector = new ChunkCollector();
        try
        {
            await using (session)
            {
                using var dataSubscription = session.DataReceived.Subscribe(data =>
                    collector.Add(DateTime.Now, data.Value));

                // 断线事件也要记录，便于判断「写命令导致耳机断开」等失败原因。
                void OnConnectionLost() =>
                    Report(progress, service.ServiceName, "INFO", "连接断开（ConnectionLost 事件）。", phases);
                session.ConnectionLost += OnConnectionLost;
                try
                {
                    // ① 连接推帧采集。
                    await Task.Delay(PushWait, cancellationToken);
                    AddCounts(progress, phases, collector, "推帧采集", ref niceCount, ref yuandaoCount);

                    // ② NiceHCK 查询组。
                    foreach (var (frame, label) in NiceHckQueries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Report(progress, service.ServiceName, "SEND", $"{label}：{FormatHex(frame)}", phases);
                        try
                        {
                            await session.WriteAsync(frame, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "写 {Label} 命令失败：{Service}", label, service.ServiceId);
                            Report(progress, service.ServiceName, "INFO", $"写命令失败：{ex.Message}（停止本组后续命令）", phases);
                            break;
                        }

                        await Task.Delay(NiceHckPacing, cancellationToken);
                    }

                    await Task.Delay(NiceHckWait, cancellationToken);
                    AddCounts(progress, phases, collector, "NiceHCK 查询组响应", ref niceCount, ref yuandaoCount);

                    // ③ 原道变体查询组。
                    foreach (var (frame, label) in YuandaoQueries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Report(progress, service.ServiceName, "SEND", $"{label}：{FormatHex(frame)}", phases);
                        try
                        {
                            await session.WriteAsync(frame, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "写 {Label} 命令失败：{Service}", label, service.ServiceId);
                            Report(progress, service.ServiceName, "INFO", $"写命令失败：{ex.Message}（停止本组后续命令）", phases);
                            break;
                        }

                        await Task.Delay(YuandaoPacing, cancellationToken);
                    }

                    await Task.Delay(YuandaoWait, cancellationToken);
                    AddCounts(progress, phases, collector, "原道变体查询组响应", ref niceCount, ref yuandaoCount);
                }
                finally
                {
                    session.ConnectionLost -= OnConnectionLost;
                }
            }

            Report(progress, service.ServiceName, "INFO", "流已关闭。", phases);
        }
        catch (Exception ex)
        {
            // 单服务异常兜底：完整堆栈进日志，校验整体继续。
            _logger.LogError(ex, "校验 {Service} 过程中异常", service.ServiceId);
            Report(progress, service.ServiceName, "INFO", $"校验过程中异常：{ex}", phases);
        }

        return new VerifyServiceResult
        {
            ServiceId = service.ServiceId,
            ServiceName = service.ServiceName,
            OpenResult = "成功",
            Phases = phases,
            NiceHckFrameCount = niceCount,
            YuandaoFrameCount = yuandaoCount,
            Format = GuessFormat(niceCount, yuandaoCount),
        };
    }

    /// <summary>分析当前阶段收集的数据块并累加到总计数。</summary>
    private static void AddCounts(
        IProgress<VerifyPhase>? progress,
        List<VerifyPhase> phases,
        ChunkCollector collector,
        string stage,
        ref int niceCount,
        ref int yuandaoCount)
    {
        var (nice, yuandao) = Analyze(collector.Snapshot(), stage, progress, phases);
        niceCount += nice;
        yuandaoCount += yuandao;
    }

    /// <summary>
    /// 分析一块数据：每块带时间戳逐块记录（RECV 明细）→ 拼合后双解析器计数与语义摘要。
    /// </summary>
    private static (int Nice, int Yuandao) Analyze(
        IReadOnlyList<(DateTime Time, byte[] Data)> chunks,
        string stage,
        IProgress<VerifyPhase>? progress,
        List<VerifyPhase> phases)
    {
        if (chunks.Count == 0)
        {
            Report(progress, stage, "RECV", "无响应。", phases);
            return (0, 0);
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            var (time, data) = chunks[i];
            Report(progress, stage, "RECV",
                $"第 {i + 1} 块（{time:HH:mm:ss.fff}，{data.Length} 字节）：{TruncateHex(FormatHex(data))}", phases);
        }

        var combined = chunks.SelectMany(c => c.Data).ToArray();
        var niceMessages = new NiceHckFrameParser().Feed(combined);
        var yuandaoMessages = new YuandaoFrameParser().Feed(combined);

        var sb = new StringBuilder();
        sb.Append("合计 ").Append(combined.Length).Append(" 字节：").Append(TruncateHex(FormatHex(combined)));
        if (niceMessages.Count > 0)
        {
            sb.Append(" ｜ NiceHCK 解析 ").Append(niceMessages.Count).Append(" 帧：");
            sb.AppendJoin("；", niceMessages.Select(NiceHckFrameSemantics.FormatFrame));
        }

        if (yuandaoMessages.Count > 0)
        {
            sb.Append(" ｜ 原道解析 ").Append(yuandaoMessages.Count).Append(" 帧：");
            sb.AppendJoin("；", yuandaoMessages.Select(YuandaoFrameSemantics.FormatFrame));
        }

        if (niceMessages.Count == 0 && yuandaoMessages.Count == 0)
        {
            sb.Append(" ｜ 两种格式均无法解析。");
        }

        Report(progress, stage, "RECV", sb.ToString(), phases);
        return (niceMessages.Count, yuandaoMessages.Count);
    }

    /// <summary>按两种解析器命中帧数判定格式（NiceHCK 头校验严格，命中优先）。</summary>
    private static ProtocolFormatGuess GuessFormat(int niceCount, int yuandaoCount) =>
        niceCount > 0 && niceCount >= yuandaoCount ? ProtocolFormatGuess.NiceHck
        : yuandaoCount > 0 ? ProtocolFormatGuess.YuandaoVariant
        : ProtocolFormatGuess.NoResponse;

    /// <summary>汇总全部服务结果，输出结论文本（含下一步建议）。</summary>
    private static string BuildConclusion(
        HeadsetDevice device,
        IReadOnlyList<VerifyServiceResult> results)
    {
        var first = results.FirstOrDefault(
            r => r.Format is ProtocolFormatGuess.NiceHck or ProtocolFormatGuess.YuandaoVariant);
        if (first is null)
        {
            return "未确认控制通道：所有候选服务均无有效响应。请确认耳机已开机配对且在蓝牙范围内（可先播放音频唤醒）；"
                + "若开流本身失败，多为系统 SPP 访问限制，需 MSIX 打包（见交接文档坑 13）。";
        }

        if (first.Format == ProtocolFormatGuess.NiceHck)
        {
            return $"控制通道确认：{first.ServiceName}（{first.ServiceId}）走 NiceHCK/BES 协议格式（4E 头）。"
                + "电量/降噪命令可直接照搬 docs/protocol/nicehck-bes-protocol.md，填入 YuandaoProtocol 解锁 M2/M3。"
                + $"（设备：{device.Name} {device.Address}）";
        }

        return $"控制通道疑似：{first.ServiceName}（{first.ServiceId}）走原道变体格式（03 头）。"
            + "需继续盲探 id→功能映射（自动校验已发 03 03/01/02/04/05 查询），或 JADX 原道 APP 提取命令字节。";
    }

    private static void Report(
        IProgress<VerifyPhase>? progress,
        string stage,
        string direction,
        string text,
        List<VerifyPhase>? phases = null)
    {
        var phase = new VerifyPhase
        {
            Stage = stage,
            Direction = direction,
            Text = text,
            Time = DateTime.Now.ToString("HH:mm:ss.fff"),
        };
        progress?.Report(phase);
        phases?.Add(phase);
    }

    private static string FormatHex(byte[] data) =>
        string.Join(" ", data.Select(b => b.ToString("X2")));

    private static string TruncateHex(string hex) =>
        hex.Length <= MaxHexPerLine * 3 ? hex : hex[..(MaxHexPerLine * 3)] + " …（已截断）";
}

/// <summary>
/// 接收块收集器：按到达时间记录每一块原始数据，供阶段分析时逐块输出明细。
/// 线程安全（读循环与 UI/编排线程并发访问）。
/// </summary>
internal sealed class ChunkCollector
{
    private readonly List<(DateTime Time, byte[] Data)> _chunks = [];

    public void Add(DateTime time, byte[] data)
    {
        lock (_chunks)
        {
            _chunks.Add((time, data));
        }
    }

    /// <summary>清空并返回全部块（此后收到的数据属于下一阶段）。</summary>
    public IReadOnlyList<(DateTime Time, byte[] Data)> Snapshot()
    {
        lock (_chunks)
        {
            var copy = _chunks.ToArray();
            _chunks.Clear();
            return copy;
        }
    }
}
