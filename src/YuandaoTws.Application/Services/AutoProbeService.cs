using System.Text;
using Microsoft.Extensions.Logging;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Exceptions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Application.Services;

/// <summary>
/// 全自动协议试探（probe v5）：翻转定位版。
/// v4 确认 fe2c1235 的 15 种写入均不改变 fe2c1239/fe2c123a（会话初值 26 31 27 81 恒稳），
/// v3r1 中 01→02 翻转的剩余候选 = v3r1 独有写入：`04`、各值 ×3 重复、fe2c123a 矩阵的 `AA×16`/`55×16`。
/// v5 对全部候选写入（每个 ×2）做逐步状态跟踪，定位翻转 `01↔02` 的具体写入。
/// 逆向完成后可将本服务的候选表移除（协议已填入适配器）。
/// </summary>
public sealed class AutoProbeService
{
    private static readonly Guid Fe2c1234 = new("fe2c1234-8366-4814-8eb0-01de32100bea");
    private static readonly Guid Fe2c1235 = new("fe2c1235-8366-4814-8eb0-01de32100bea");
    private static readonly Guid Fe2c1236 = new("fe2c1236-8366-4814-8eb0-01de32100bea");
    private static readonly Guid Fe2c1237 = new("fe2c1237-8366-4814-8eb0-01de32100bea");
    private static readonly Guid Fe2c1239 = new("fe2c1239-8366-4814-8eb0-01de32100bea");
    private static readonly Guid Fe2c123a = new("fe2c123a-8366-4814-8eb0-01de32100bea");
    private static readonly Guid Fe2c7777 = new("77777777-7777-7777-7777-777777777777");

    // fe2c1237 仅支持无响应写（v2 确认）。
    private static readonly HashSet<Guid> WriteWithoutResponseOnly = [Fe2c1237];

    // 疑似状态寄存器：字节 0 = 01/02 状态位（v3 确认可被写入翻转、跨重连保持）。
    private static readonly Guid? StateRegisterCharacteristic = Fe2c1239;

    // 会话特征：写入会改变其读值（v2 确认），与响应计算相关但非每次连接随机（v3 分析）。
    private static readonly Guid? SessionCharacteristic = Fe2c123a;

    // 跟踪值（probe v5 翻转定位）：v4 已确认 fe2c1235 的 15 种写入均不翻转 1239，
    // 剩余候选 = v3r1 独有写入（04 / ×3 重复 / AA×16 / 55×16 到 fe2c123a）。
    // 每个值 ×2 以捕获一次性翻转。
    private static readonly byte[][] TrackedValues =
    [
        [0x04], [0x04],
        [0x02, 0x00], [0x02, 0x00],
        [0xFE, 0x01], [0xAA, 0x01],
        [0x02, 0x01],
        [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10],
        [0x01], [0x02], [0x02], [0x03], [0x05], [0x00], [0xFF],
    ];

    // fe2c123a 矩阵负载（v5）：AA×16 与 55×16 是 v3r1 独有、翻转候选，放最前并 ×2。
    private static readonly byte[][] SessionPayloads =
    [
        [0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA],
        [0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55],
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
    ];

    private const int ResponseWindowMs = 450;
    private readonly HeadsetConnectionService _connectionService;
    private readonly ILogger<AutoProbeService> _logger;
    private readonly object _notifyLock = new();
    private readonly List<(DateTimeOffset Time, GattNotification Notification)> _notifyLog = new();

    // 状态翻转记录：<写入特征, 写入值, 翻转方向>。
    private readonly List<string> _flipLog = new();

    public AutoProbeService(
        HeadsetConnectionService connectionService,
        ILogger<AutoProbeService> logger)
    {
        _connectionService = connectionService;
        _logger = logger;
    }

    /// <summary>运行一次全自动试探（probe v4），返回对照报告文本。</summary>
    public async Task<string> RunAutoProbeAsync(CancellationToken cancellationToken)
    {
        var session = _connectionService.CurrentSession
            ?? throw new BluetoothConnectionException("未连接设备，无法自动探测。");

        lock (_notifyLock)
        {
            _notifyLog.Clear();
        }

        _flipLog.Clear();

        using var subscription = session.Notifications.Subscribe(notification =>
        {
            lock (_notifyLock)
            {
                _notifyLog.Add((DateTimeOffset.Now, notification));
            }
        });

        var services = await session.EnumerateServicesAsync(cancellationToken);
        var notifyCharacteristics = services
            .SelectMany(s => s.Characteristics)
            .Where(c => c.Properties.HasFlag(GattCharacteristicProperties.Notify)
                     || c.Properties.HasFlag(GattCharacteristicProperties.Indicate))
            .ToArray();

        foreach (var characteristic in notifyCharacteristics)
        {
            try
            {
                await session.SubscribeAsync(characteristic.Uuid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("订阅 {Uuid} 失败：{Message}", characteristic.Uuid, ex.Message);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("原道「原点」协议试探报告 v5（翻转定位）");
        sb.AppendLine($"设备：{session.Device.Name}    地址：{session.Device.Address}");
        sb.AppendLine($"枚举服务数：{services.Count}，已订阅通知特征：{notifyCharacteristics.Length}");
        sb.AppendLine("每行：写入 → fe2c1239 前后 | fe2c123a 前后 | 通知。★ = 该次写入改变了对应寄存器。");
        sb.AppendLine(new string('=', 78));

        // A. fe2c1235 逐写状态跟踪。
        sb.AppendLine();
        sb.AppendLine("A. fe2c1235 逐写状态跟踪（02 连写 2 次测翻转，01/0100/16B 重复测稳定）");
        foreach (var value in TrackedValues)
        {
            await TrackWriteAsync(sb, session, Fe2c1235, value, cancellationToken);
        }

        // B. fe2c123a 矩阵（带状态跟踪）。
        if (SessionCharacteristic is Guid sessionChar)
        {
            sb.AppendLine();
            sb.AppendLine($"B. {sessionChar} 会话写矩阵（带状态跟踪）");
            var baseline = await SafeReadAsync(session, sessionChar, cancellationToken);
            sb.AppendLine($"  初始读值：{FormatHex(baseline ?? [])}");
            var payloads = new List<(string Label, byte[]? Payload)>
            {
                ("回写当前读值", baseline),
                ("全 AA", SessionPayloads[0]),
                ("全 AA（再写）", SessionPayloads[0]),
                ("全 55", SessionPayloads[1]),
                ("全 55（再写）", SessionPayloads[1]),
                ("全 0", SessionPayloads[2]),
                ("全 FF", SessionPayloads[3]),
            };

            foreach (var (label, payload) in payloads)
            {
                if (payload is null)
                {
                    continue;
                }

                var before = await ReadStateAsync(session, cancellationToken);
                var notifyStart = NotifyLogCount();
                try
                {
                    await session.WriteAsync(sessionChar, payload, cancellationToken);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  {label} → 写入失败：{Describe(ex)}");
                    continue;
                }

                await Task.Delay(ResponseWindowMs, cancellationToken);
                var responses = TakeNotifySince(notifyStart);
                var after = await ReadStateAsync(session, cancellationToken);
                sb.AppendLine($"  {label} → 1239 {StateLine(before.R1239, after.R1239)}；123a {StateLine(before.R123a, after.R123a)}；通知 {responses.Count} 条{(responses.Count > 0 ? $"（{FormatHex(responses[0].Notification.Value)}）" : string.Empty)}");

                // 每次矩阵写入后探测 fe2c1235 01，看响应与状态的关系（123a 变化一并记录）。
                var probeBefore = await ReadStateAsync(session, cancellationToken);
                var probe = await ProbeAsync(session, Fe2c1235, [0x01], cancellationToken);
                var probeAfter = await ReadStateAsync(session, cancellationToken);
                sb.AppendLine($"      写 01 探测 → 1239 {StateLine(probeBefore.R1239, probeAfter.R1239)}；123a {StateLine(probeBefore.R123a, probeAfter.R123a)}；响应 {FormatHex(probe ?? [])}");
            }
        }

        // C. 跨特征写（带状态跟踪）。
        sb.AppendLine();
        sb.AppendLine("C. 跨特征写（FF×16）是否改变状态");
        foreach (var (featureUuid, withResponse) in new[]
        {
            (Fe2c1237, false),
            (Fe2c7777, true),
            (Fe2c1234, true),
            (Fe2c1236, true),
        })
        {
            await TrackWriteAsync(sb, session, featureUuid, All(0xFF, 16), cancellationToken, withResponse);
        }

        // 汇总：状态翻转记录。
        sb.AppendLine();
        sb.AppendLine("状态翻转记录（fe2c1239 字节 0 变化）：");
        if (_flipLog.Count == 0)
        {
            sb.AppendLine("  （本次运行未观察到翻转）");
        }
        else
        {
            foreach (var flip in _flipLog)
            {
                sb.AppendLine($"  ★ {flip}");
            }
        }

        _logger.LogInformation("probe v4 报告生成完成，{Bytes} 字节", sb.Length);
        return sb.ToString();
    }

    /// <summary>写一个值，记录前后状态与通知，检测 fe2c1239 字节 0 翻转。</summary>
    private async Task TrackWriteAsync(
        StringBuilder sb,
        IGattDeviceSession session,
        Guid featureUuid,
        byte[] value,
        CancellationToken cancellationToken,
        bool withResponse = true)
    {
        var before = await ReadStateAsync(session, cancellationToken);
        var notifyStart = NotifyLogCount();
        try
        {
            await session.WriteAsync(featureUuid, value, cancellationToken, withResponse);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  写 {FormatHex(value)} → 失败：{Describe(ex)}");
            return;
        }

        await Task.Delay(ResponseWindowMs, cancellationToken);
        var responses = TakeNotifySince(notifyStart);
        var after = await ReadStateAsync(session, cancellationToken);

        var stateLine = $"1239 {StateLine(before.R1239, after.R1239)}；123a {StateLine(before.R123a, after.R123a)}";
        sb.AppendLine($"  写 {FormatHex(value)} → {stateLine}；通知 {responses.Count} 条{(responses.Count > 0 ? $"（{FormatHex(responses[0].Notification.Value)}）" : string.Empty)}");

        if (before.R1239 is { Length: > 0 } b
            && after.R1239 is { Length: > 0 } a
            && b[0] != a[0])
        {
            _flipLog.Add($"{featureUuid} 写 {FormatHex(value)} → 1239 字节0 {b[0]:X2}→{a[0]:X2}");
        }
    }

    /// <summary>读取 fe2c1239 与 fe2c123a 当前值。</summary>
    private async Task<(byte[]? R1239, byte[]? R123a)> ReadStateAsync(IGattDeviceSession session, CancellationToken ct)
    {
        var r1239 = StateRegisterCharacteristic is Guid r ? await SafeReadAsync(session, r, ct) : null;
        var r123a = SessionCharacteristic is Guid s ? await SafeReadAsync(session, s, ct) : null;
        return (r1239, r123a);
    }

    /// <summary>"旧→新（★变化）" 或 "不变" 的状态行；null 显示读取失败。</summary>
    private static string StateLine(byte[]? before, byte[]? after)
    {
        if (before is null || after is null)
        {
            return "读取失败";
        }

        if (before.SequenceEqual(after))
        {
            return $"不变（{FormatHex(after)}）";
        }

        return $"{FormatHex(before)} → {FormatHex(after)} ★";
    }

    /// <summary>
    /// 状态观察模式：30 秒内每秒读取疑似状态寄存器与会话特征，同时记录期间收到的所有通知。
    /// </summary>
    public async Task<string> ObserveStateAsync(CancellationToken cancellationToken)
    {
        var session = _connectionService.CurrentSession
            ?? throw new BluetoothConnectionException("未连接设备，无法观察。");

        if (StateRegisterCharacteristic is not Guid stateRegister)
        {
            throw new BluetoothConnectionException("未配置状态寄存器特征。");
        }

        lock (_notifyLock)
        {
            _notifyLog.Clear();
        }

        using var subscription = session.Notifications.Subscribe(notification =>
        {
            lock (_notifyLock)
            {
                _notifyLog.Add((DateTimeOffset.Now, notification));
            }
        });

        var sb = new StringBuilder();
        sb.AppendLine("原道「原点」状态观察报告（30 秒）");
        sb.AppendLine($"设备：{session.Device.Name}    地址：{session.Device.Address}");
        sb.AppendLine("期间请戴上耳机，用触控长按切换「降噪 / 通透 / 关闭」，来回多试几次。");
        sb.AppendLine(new string('=', 78));

        for (var i = 0; i < 30; i++)
        {
            var time = DateTimeOffset.Now;
            var value = await SafeReadAsync(session, stateRegister, cancellationToken);
            var sessionValue = SessionCharacteristic is Guid sessionChar
                ? await SafeReadAsync(session, sessionChar, cancellationToken)
                : null;
            sb.AppendLine($"[{time:HH:mm:ss}] 状态寄存器 {stateRegister}：{(value is null ? "读取失败" : FormatHex(value))}"
                + (sessionValue is null ? string.Empty : $"    会话特征：{FormatHex(sessionValue)}"));

            List<(DateTimeOffset Time, GattNotification Notification)> pending;
            lock (_notifyLock)
            {
                pending = _notifyLog.ToList();
                _notifyLog.Clear();
            }

            foreach (var (_, notification) in pending)
            {
                sb.AppendLine($"    ★ 通知 {notification.CharacteristicUuid}：{FormatHex(notification.Value)}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        return sb.ToString();
    }

    /// <summary>写一个值并返回收到的第一条通知响应（无响应或失败返回 null）。</summary>
    private async Task<byte[]?> ProbeAsync(
        IGattDeviceSession session,
        Guid featureUuid,
        byte[] value,
        CancellationToken cancellationToken,
        bool withResponse = true)
    {
        var notifyStart = NotifyLogCount();
        try
        {
            await session.WriteAsync(featureUuid, value, cancellationToken, withResponse);
        }
        catch
        {
            return null;
        }

        await Task.Delay(ResponseWindowMs, cancellationToken);
        var responses = TakeNotifySince(notifyStart);
        return responses.Count == 0 ? null : responses[0].Notification.Value;
    }

    private int NotifyLogCount()
    {
        lock (_notifyLock)
        {
            return _notifyLog.Count;
        }
    }

    private List<(DateTimeOffset Time, GattNotification Notification)> TakeNotifySince(int start)
    {
        lock (_notifyLock)
        {
            return _notifyLog.Skip(start).ToList();
        }
    }

    private static async Task<byte[]?> SafeReadAsync(IGattDeviceSession session, Guid uuid, CancellationToken ct)
    {
        try
        {
            return await session.ReadAsync(uuid, ct);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatHex(byte[] bytes) =>
        bytes.Length == 0 ? "（空）" : string.Join(" ", bytes.Select(b => b.ToString("X2")));

    private static string Describe(Exception ex) =>
        $"[{ex.GetType().Name} 0x{ex.HResult:X8}] {ex.Message}";

    private static byte[] All(byte value, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }
}
