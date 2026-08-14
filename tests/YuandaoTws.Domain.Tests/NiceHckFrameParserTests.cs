using FluentAssertions;
using YuandaoTws.Domain;

namespace YuandaoTws.Domain.Tests;

/// <summary>
/// NiceHCK/BES 协议帧测试。命令字节断言对照开源项目 tests/protocol.rs 逐条抄录，
/// 帧样例来自 docs/protocol/nicehck-bes-protocol.md §2。
/// </summary>
public class NiceHckFrameParserTests
{
    [Fact]
    public void QueryFirmware_命令字节与开源项目一致()
    {
        NiceHckCommands.QueryFirmware().Should().Equal(0x4E, 0x03, 0x00, 0x00, 0x03, 0x00);
    }

    [Fact]
    public void QueryBattery_命令字节与开源项目一致()
    {
        NiceHckCommands.QueryBattery().Should().Equal(0x4E, 0x03, 0x00, 0x00, 0x05, 0x00);
    }

    [Fact]
    public void QueryAnc_命令字节与开源项目一致()
    {
        NiceHckCommands.QueryAnc().Should().Equal(0x4E, 0x03, 0x00, 0x00, 0x01, 0x01);
    }

    [Fact]
    public void QueryEq_命令字节与开源项目一致()
    {
        NiceHckCommands.QueryEq().Should().Equal(0x4E, 0x03, 0x00, 0x00, 0x07, 0x01);
    }

    [Fact]
    public void QueryGameMode_命令字节与开源项目一致()
    {
        NiceHckCommands.QueryGameMode().Should().Equal(0x4E, 0x03, 0x00, 0x00, 0x08, 0x01);
    }

    [Fact]
    public void QueryLowLatency_命令字节与开源项目一致()
    {
        NiceHckCommands.QueryLowLatency().Should().Equal(0x4E, 0x03, 0x00, 0x00, 0x06, 0x01);
    }

    [Fact]
    public void SetAnc_命令字节与开源项目一致()
    {
        NiceHckCommands.SetAnc(0x01).Should().Equal(0x4E, 0x05, 0x00, 0x00, 0x01, 0x02, 0x01, 0x00);
        NiceHckCommands.SetAnc(0x03).Should().Equal(0x4E, 0x05, 0x00, 0x00, 0x01, 0x02, 0x03, 0x00);
    }

    [Fact]
    public void Feed_电量响应帧解析出Op与载荷()
    {
        var messages = new NiceHckFrameParser().Feed([0x4E, 0x06, 0x00, 0x00, 0x05, 0x00, 0x50, 0x4B, 0x3C]);

        messages.Should().HaveCount(1);
        messages[0].OpCode.Should().Be(NiceHckOp.Battery);
        messages[0].Payload.Should().Equal(0x50, 0x4B, 0x3C);
    }

    [Fact]
    public void Feed_粘包一次喂两帧切出两帧()
    {
        var messages = new NiceHckFrameParser().Feed(
            [0x4E, 0x03, 0x00, 0x00, 0x03, 0x00, 0x4E, 0x04, 0x00, 0x00, 0x01, 0x01, 0x03]);

        messages.Should().HaveCount(2);
        messages[0].OpCode.Should().Be(NiceHckOp.Version);
        messages[1].OpCode.Should().Be(NiceHckOp.AncQuery);
        messages[1].Payload.Should().Equal(0x03);
    }

    [Fact]
    public void Feed_拆包分两次喂拼出完整帧()
    {
        var parser = new NiceHckFrameParser();
        parser.Feed([0x4E, 0x06, 0x00, 0x00]).Should().BeEmpty();

        var messages = parser.Feed([0x05, 0x00, 0x50, 0x4B, 0x3C]);

        messages.Should().HaveCount(1);
        messages[0].OpCode.Should().Be(NiceHckOp.Battery);
        messages[0].Payload.Should().Equal(0x50, 0x4B, 0x3C);
    }

    [Fact]
    public void Feed_脏数据自动重同步到魔数()
    {
        var parser = new NiceHckFrameParser();
        parser.Feed([0x00, 0xFF, 0x4E, 0x04, 0x00, 0x00, 0x01]).Should().BeEmpty();

        // 残余半帧 + 另一完整帧连续到达（照抄开源项目 tests/protocol.rs 场景）。
        var messages = parser.Feed([0x01, 0x03, 0x4E, 0x04, 0x00, 0x00, 0x06, 0x01, 0x01]);

        messages.Should().HaveCount(2);
        messages[0].OpCode.Should().Be(NiceHckOp.AncQuery);
        messages[0].Payload.Should().Equal(0x03);
        messages[1].OpCode.Should().Be(NiceHckOp.LowLatencyQuery);
        messages[1].Payload.Should().Equal(0x01);
    }

    [Fact]
    public void Feed_长度字段非法时逐字节重同步()
    {
        var messages = new NiceHckFrameParser().Feed(
            [0x4E, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x4E, 0x04, 0x00, 0x00, 0x01, 0x01, 0x03]);

        messages.Should().HaveCount(1);
        messages[0].OpCode.Should().Be(NiceHckOp.AncQuery);
        messages[0].Payload.Should().Equal(0x03);
    }

    [Fact]
    public void Feed_无魔数数据不产出帧()
    {
        new NiceHckFrameParser().Feed([0x01, 0x02, 0x03, 0x04]).Should().BeEmpty();
    }

    [Fact]
    public void Describe_电量帧解析左右耳与盒电量()
    {
        var message = new NiceHckMessage { OpCode = NiceHckOp.Battery, Payload = [80, 75, 60] };

        NiceHckFrameSemantics.Describe(message).Should().Be("电量：左 80% 右 75% 盒 60%");
    }

    [Fact]
    public void Describe_盒电量为0表示未知()
    {
        var message = new NiceHckMessage { OpCode = NiceHckOp.Battery, Payload = [80, 75, 0] };

        NiceHckFrameSemantics.Describe(message).Should().Be("电量：左 80% 右 75% 盒 未知");
    }

    [Fact]
    public void Describe_电量字节bit7为充电标志()
    {
        // 实测帧：E4 E4 64（verify 日志），E4 & 0x7F = 100，bit7=1 表示充电中。
        var message = new NiceHckMessage { OpCode = NiceHckOp.Battery, Payload = [0xE4, 0xE4, 0x64] };

        NiceHckFrameSemantics.Describe(message).Should().Be("电量：左 100%(充电中) 右 100%(充电中) 盒 100%");
    }

    [Fact]
    public void Describe_ANC帧解析模式名()
    {
        var message = new NiceHckMessage { OpCode = NiceHckOp.AncQuery, Payload = [0x11] };

        NiceHckFrameSemantics.Describe(message).Should().Be("降噪模式：风噪抑制");
    }

    [Fact]
    public void Describe_固件帧解析版本()
    {
        var message = new NiceHckMessage { OpCode = NiceHckOp.Version, Payload = [8, 4] };

        NiceHckFrameSemantics.Describe(message).Should().Be("固件版本：4.8");
    }

    [Fact]
    public void Describe_未知Op返回null()
    {
        var message = new NiceHckMessage { OpCode = 0x0103, Payload = [1, 2, 3] };

        NiceHckFrameSemantics.Describe(message).Should().BeNull();
    }

    [Fact]
    public void FormatFrame_重建完整帧字节()
    {
        var message = new NiceHckMessage { OpCode = NiceHckOp.Battery, Payload = [0x64, 0x64, 0x64] };

        NiceHckFrameSemantics.FormatFrame(message)
            .Should().Be("4E 06 00 00 05 00 64 64 64 = 电量：左 100% 右 100% 盒 100%");
    }
}
