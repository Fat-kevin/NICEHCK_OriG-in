using FluentAssertions;
using YuandaoTws.Domain;
using YuandaoTws.Domain.Enums;
using YuandaoTws.Domain.Models;
using YuandaoTws.Infrastructure.Protocols;

namespace YuandaoTws.Domain.Tests;

/// <summary>原道 OriG in 已确认 RFCOMM/NiceHCK 协议的适配器测试。</summary>
public class YuandaoProtocolTests
{
    private static YuandaoProtocol Create() => new();

    [Fact]
    public void 控制服务UUID为真机确认的RFCOMM端点()
    {
        Create().ServiceUuid.Should().Be(new Guid("0000a100-1000-8000-4e48-434b4354524c"));
    }

    [Theory]
    [InlineData("YUANDAO OriG in")]
    [InlineData("OriG in")]
    public void 原道设备名命中协议(string name)
    {
        var device = new HeadsetDevice { Name = name, DeviceId = "id", Address = "AA:BB:CC:DD:EE:FF", IsLowEnergy = false };
        Create().Matches(device).Should().BeTrue();
    }

    [Fact]
    public void 非原道设备不命中协议()
    {
        var device = new HeadsetDevice { Name = "Other headset", DeviceId = "id", Address = "AA:BB:CC:DD:EE:FF", IsLowEnergy = false };
        Create().Matches(device).Should().BeFalse();
    }

    [Fact]
    public void 主控4E电量帧按公开协议直接解析三个百分比()
    {
        var update = Create().TryParse(new NiceHckMessage { OpCode = NiceHckOp.Battery, Payload = [0x64, 0x5A, 0x32] });

        update!.Battery!.LeftEarPercent.Should().Be(100);
        update.Battery.RightEarPercent.Should().Be(90);
        update.Battery.CasePercent.Should().Be(50);
        update.Battery.IsLeftEarCharging.Should().BeNull();
        update.Battery.IsRightEarCharging.Should().BeNull();
    }

    [Fact]
    public void 盒电量原始0解析为未知()
    {
        var update = Create().TryParse(new NiceHckMessage { OpCode = NiceHckOp.Battery, Payload = [0x64, 0x64, 0x00] });
        update!.Battery!.CasePercent.Should().BeNull();
    }

    [Fact]
    public void ANC六态命令均按协议构造()
    {
        var protocol = Create();
        protocol.BuildAncCommand(NoiseCancellingMode.Off).Should().Equal(0x4E, 0x05, 0, 0, 1, 2, 0, 0);
        protocol.BuildAncCommand(NoiseCancellingMode.Transparency).Should().Equal(0x4E, 0x05, 0, 0, 1, 2, 1, 0);
        protocol.BuildAncCommand(NoiseCancellingMode.Normal).Should().Equal(0x4E, 0x05, 0, 0, 1, 2, 2, 0);
        protocol.BuildAncCommand(NoiseCancellingMode.Deep).Should().Equal(0x4E, 0x05, 0, 0, 1, 2, 3, 0);
        protocol.BuildAncCommand(NoiseCancellingMode.Experimental).Should().Equal(0x4E, 0x05, 0, 0, 1, 2, 0x10, 0);
        protocol.BuildAncCommand(NoiseCancellingMode.WindSuppression).Should().Equal(0x4E, 0x05, 0, 0, 1, 2, 0x11, 0);
    }

    [Fact]
    public void 真机ANC响应解析为深度降噪()
    {
        var update = Create().TryParse(new NiceHckMessage { OpCode = NiceHckOp.AncQuery, Payload = [0x03] });
        update!.AncMode.Should().Be(NoiseCancellingMode.Deep);
    }

    [Fact]
    public void 未知ANC值保留为Unknown()
    {
        var update = Create().TryParse(new NiceHckMessage { OpCode = NiceHckOp.AncQuery, Payload = [0x7F] });
        update!.AncMode.Should().Be(NoiseCancellingMode.Unknown);
    }

    [Fact]
    public void 固件48解锁扩展能力()
    {
        var update = Create().TryParse(new NiceHckMessage { OpCode = NiceHckOp.Version, Payload = [0x08, 0x04] });
        update!.Firmware!.ToString().Should().Be("4.8");
        update.Firmware.SupportsModernCodecAndExtendedEq.Should().BeTrue();
    }
}
