namespace YuandaoTws.Domain.Models;

/// <summary>校验对某服务收到的数据判定的协议格式。</summary>
public enum ProtocolFormatGuess
{
    /// <summary>尚未判定。</summary>
    Unknown,

    /// <summary>NiceHCK/BES 协议格式（4E 头 6 字节）。</summary>
    NiceHck,

    /// <summary>原道变体格式（03 头 4 字节）。</summary>
    YuandaoVariant,

    /// <summary>无任何响应，无法判定。</summary>
    NoResponse,
}

/// <summary>校验过程中的一个阶段记录（方向 + hex + 解析摘要），用于 UI 日志与报告。</summary>
public sealed record VerifyPhase
{
    /// <summary>阶段名，如「推帧采集」「NiceHCK 查询组」。</summary>
    public required string Stage { get; init; }

    /// <summary>方向：SEND / RECV / INFO。</summary>
    public required string Direction { get; init; }

    /// <summary>内容（hex 或说明文本）。</summary>
    public required string Text { get; init; }

    /// <summary>时间戳（HH:mm:ss.fff）。</summary>
    public required string Time { get; init; }
}

/// <summary>对一个候选 SPP 服务的完整校验结果。</summary>
public sealed record VerifyServiceResult
{
    public required Guid ServiceId { get; init; }

    public required string ServiceName { get; init; }

    /// <summary>开流结果（"成功" 或错误信息）。</summary>
    public required string OpenResult { get; init; }

    /// <summary>各阶段记录（含收发与解析摘要）。</summary>
    public required IReadOnlyList<VerifyPhase> Phases { get; init; }

    /// <summary>所有阶段中按 NiceHCK 格式解析出的帧总数。</summary>
    public required int NiceHckFrameCount { get; init; }

    /// <summary>所有阶段中按原道变体格式解析出的帧总数。</summary>
    public required int YuandaoFrameCount { get; init; }

    public required ProtocolFormatGuess Format { get; init; }
}

/// <summary>一轮自动校验的完整报告。</summary>
public sealed record VerifyReport
{
    public required string DeviceName { get; init; }

    public required string DeviceAddress { get; init; }

    public required DateTime StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    public required IReadOnlyList<VerifyServiceResult> Services { get; init; }

    /// <summary>最终结论文本（含下一步建议）。</summary>
    public required string Conclusion { get; init; }
}
