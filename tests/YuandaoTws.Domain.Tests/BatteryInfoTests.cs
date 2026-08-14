using FluentAssertions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Domain.Tests;

public class BatteryInfoTests
{
    [Fact]
    public void FromSingleValue_左右耳同值且来源为标准()
    {
        var info = BatteryInfo.FromSingleValue(75);

        info.LeftEarPercent.Should().Be(75);
        info.RightEarPercent.Should().Be(75);
        info.CasePercent.Should().BeNull();
        info.Source.Should().Be(BatterySource.Standard);
    }

    [Fact]
    public void FromSingleValue_合并电量等于该值()
    {
        var info = BatteryInfo.FromSingleValue(60);

        info.CombinedPercent.Should().Be(60);
        info.HasAny.Should().BeTrue();
    }

    [Fact]
    public void FromLeftRight_保留左右耳独立值与私有来源()
    {
        var info = BatteryInfo.FromLeftRight(80, 65, 90);

        info.LeftEarPercent.Should().Be(80);
        info.RightEarPercent.Should().Be(65);
        info.CasePercent.Should().Be(90);
        info.Source.Should().Be(BatterySource.Private);
    }

    [Fact]
    public void FromLeftRight_合并电量优先左耳()
    {
        var info = BatteryInfo.FromLeftRight(80, 65);

        info.CombinedPercent.Should().Be(80);
    }

    [Fact]
    public void FromLeftRight_左耳未知时合并电量回退右耳()
    {
        var info = BatteryInfo.FromLeftRight(null, 65);

        info.CombinedPercent.Should().Be(65);
    }

    [Fact]
    public void FromLeftRight_左右耳均未知时合并电量回退充电盒()
    {
        var info = BatteryInfo.FromLeftRight(null, null, 30);

        info.CombinedPercent.Should().Be(30);
    }

    [Fact]
    public void 全部未知时_合并电量为空且HasAny为假()
    {
        var info = BatteryInfo.FromLeftRight(null, null);

        info.CombinedPercent.Should().BeNull();
        info.HasAny.Should().BeFalse();
    }
}
