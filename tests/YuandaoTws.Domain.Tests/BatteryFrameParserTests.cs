using FluentAssertions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Domain.Tests;

public class BatteryFrameParserTests
{
    [Theory]
    [InlineData(new byte[] { 0 }, 0)]
    [InlineData(new byte[] { 100 }, 100)]
    [InlineData(new byte[] { 45 }, 45)]
    public void ParseStandard_一字节帧解析出对应电量(byte[] frame, byte expected)
    {
        var info = BatteryFrameParser.ParseStandard(frame);

        info.Should().NotBeNull();
        info!.CombinedPercent.Should().Be(expected);
        info.Source.Should().Be(BatterySource.Standard);
    }

    [Fact]
    public void ParseStandard_空帧返回null()
    {
        BatteryFrameParser.ParseStandard([]).Should().BeNull();
    }

    [Fact]
    public void ParseStandard_多字节帧仅取首字节()
    {
        var info = BatteryFrameParser.ParseStandard([50, 0xAB]);

        info!.CombinedPercent.Should().Be(50);
    }
}
