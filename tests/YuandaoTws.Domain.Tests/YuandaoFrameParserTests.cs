using FluentAssertions;
using YuandaoTws.Domain;

namespace YuandaoTws.Domain.Tests;

/// <summary>
/// 原道变体帧（03 头）解析测试。基线帧来自实测
/// （docs/protocol/yuandao-origin.md §3.12，df21fe2c 服务连接即推）。
/// </summary>
public class YuandaoFrameParserTests
{
    /// <summary>实测基线：03 01 00 03 | 44 18 D8 / 03 02 00 06 | 6C A9 CC 75 1E A4 / 03 03 00 03 | 64 64 64。</summary>
    private static readonly byte[] BaselinePush =
    [
        0x03, 0x01, 0x00, 0x03, 0x44, 0x18, 0xD8,
        0x03, 0x02, 0x00, 0x06, 0x6C, 0xA9, 0xCC, 0x75, 0x1E, 0xA4,
        0x03, 0x03, 0x00, 0x03, 0x64, 0x64, 0x64,
    ];

    [Fact]
    public void Feed_实测基线三帧全部切出且载荷正确()
    {
        var messages = new YuandaoFrameParser().Feed(BaselinePush);

        messages.Should().HaveCount(3);
        messages[0].Id.Should().Be(0x01);
        messages[0].Payload.Should().Equal(0x44, 0x18, 0xD8);
        messages[1].Id.Should().Be(0x02);
        messages[1].Payload.Should().Equal(0x6C, 0xA9, 0xCC, 0x75, 0x1E, 0xA4);
        messages[2].Id.Should().Be(0x03);
        messages[2].Payload.Should().Equal(0x64, 0x64, 0x64);
    }

    [Fact]
    public void Feed_拆包分两次喂拼出帧()
    {
        var parser = new YuandaoFrameParser();
        parser.Feed([0x03, 0x03, 0x00, 0x03]).Should().BeEmpty();

        var messages = parser.Feed([0x64, 0x64, 0x64]);

        messages.Should().HaveCount(1);
        messages[0].Id.Should().Be(0x03);
        messages[0].Payload.Should().Equal(0x64, 0x64, 0x64);
    }

    [Fact]
    public void Feed_NiceHck帧数据不误报为原道帧()
    {
        var messages = new YuandaoFrameParser().Feed(
            [0x4E, 0x06, 0x00, 0x00, 0x05, 0x00, 0x64, 0x64, 0x64]);

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Feed_保留位非零丢弃该字节()
    {
        var messages = new YuandaoFrameParser().Feed([0x03, 0x01, 0x01, 0x03, 0x44, 0x18, 0xD8]);

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Feed_id超上限不解析()
    {
        var messages = new YuandaoFrameParser().Feed([0x03, 0x21, 0x00, 0x01, 0xFF]);

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Feed_长度为零或超上限不解析()
    {
        new YuandaoFrameParser().Feed([0x03, 0x01, 0x00, 0x00]).Should().BeEmpty();
        new YuandaoFrameParser().Feed([0x03, 0x01, 0x00, 0x41]).Should().BeEmpty();
    }

    [Fact]
    public void Describe_id03三字节解析为电量()
    {
        var message = new YuandaoMessage { Id = 0x03, Payload = [0x64, 0x64, 0x64] };

        YuandaoFrameSemantics.Describe(message).Should().Be("状态电量（未含充电标志）：左 100% 右 100% 盒 100%");
    }

    [Fact]
    public void Describe_id03不把高位误判为充电标志()
    {
        // 新的逐阶段实测表明 USB 插拔前后没有任何字段变化，E4 不能解释为充电标志。
        var message = new YuandaoMessage { Id = 0x03, Payload = [0xE4, 0xE4, 0x64] };

        YuandaoFrameSemantics.Describe(message).Should().Be("状态电量（未含充电标志）：左 未知 右 未知 盒 100%");
    }

    [Fact]
    public void TryParseBattery_id03不伪造充电状态且按直接百分比解析()
    {
        var message = new YuandaoMessage { Id = 0x03, Payload = [0x5A, 0x64, 0x64] };

        var battery = YuandaoFrameSemantics.TryParseBattery(message);

        battery.Should().NotBeNull();
        battery!.LeftEarPercent.Should().Be(90);
        battery.RightEarPercent.Should().Be(100);
        battery.CasePercent.Should().Be(100);
        battery.IsLeftEarCharging.Should().BeNull();
        battery.IsRightEarCharging.Should().BeNull();
        battery.IsCaseCharging.Should().BeNull();
    }

    [Fact]
    public void Describe_未知id返回null()
    {
        var message = new YuandaoMessage { Id = 0x0A, Payload = [0x01] };

        YuandaoFrameSemantics.Describe(message).Should().BeNull();
    }

    [Fact]
    public void FormatFrame_重建完整帧字节()
    {
        var message = new YuandaoMessage { Id = 0x03, Payload = [0x64, 0x64, 0x64] };

        YuandaoFrameSemantics.FormatFrame(message)
            .Should().Be("03 03 00 03 64 64 64 = 状态电量（未含充电标志）：左 100% 右 100% 盒 100%");
    }

    [Fact]
    public void Query_构造查询帧()
    {
        YuandaoCommands.Query(0x03).Should().Equal(0x03, 0x03, 0x00, 0x00);
    }
}
