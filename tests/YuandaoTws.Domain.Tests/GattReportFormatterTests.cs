using FluentAssertions;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Domain.Tests;

public class GattReportFormatterTests
{
    [Fact]
    public void DescribeUuid_已知标准UUID返回名称与UUID()
    {
        GattReportFormatter.DescribeUuid(StandardUuids.BatteryLevel)
            .Should().Be("Battery Level (00002a19-0000-1000-8000-00805f9b34fb)");
    }

    [Fact]
    public void DescribeUuid_未知私有UUID标记私有()
    {
        var privateUuid = new Guid("8E4F12C3-1000-2000-3000-0123456789AB");

        GattReportFormatter.DescribeUuid(privateUuid)
            .Should().Be($"{privateUuid}（私有）");
    }

    [Fact]
    public void DescribeProperties_组合属性拼接中文标签()
    {
        var properties = GattCharacteristicProperties.Read
                       | GattCharacteristicProperties.Write
                       | GattCharacteristicProperties.Notify;

        GattReportFormatter.DescribeProperties(properties).Should().Be("读 / 写 / 通知");
    }

    [Fact]
    public void DescribeProperties_无属性返回无()
    {
        GattReportFormatter.DescribeProperties(GattCharacteristicProperties.None).Should().Be("无");
    }

    [Fact]
    public void FormatHex_空数组返回空标记()
    {
        GattReportFormatter.FormatHex([]).Should().Be("（空）");
    }

    [Fact]
    public void FormatHex_单字节显示hex()
    {
        GattReportFormatter.FormatHex([0x4F])
            .Should().StartWith("4F").And.EndWith("O");
    }

    [Fact]
    public void FormatHex_多字节显示hex与ASCII侧栏()
    {
        var frame = new byte[] { 0x41, 0x42, 0x43 };

        GattReportFormatter.FormatHex(frame).Should().Contain("41 42 43").And.Contain("ABC");
    }

    [Fact]
    public void BuildReport_完整报告包含设备服务特征读值与通知()
    {
        var device = new HeadsetDevice
        {
            Name = "OriG in Origin",
            DeviceId = "BluetoothLE#Foo",
            Address = "AA:BB:CC:DD:EE:FF",
            IsLowEnergy = true,
        };
        var services = new[]
        {
            new GattServiceInfo
            {
                Uuid = StandardUuids.BatteryService,
                Characteristics =
                [
                    new GattCharacteristicInfo
                    {
                        Uuid = StandardUuids.BatteryLevel,
                        Properties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                    },
                ],
            },
        };
        var readValues = new Dictionary<Guid, byte[]> { [StandardUuids.BatteryLevel] = [0x4F] };
        var notifications = new Dictionary<Guid, byte[]> { [StandardUuids.BatteryLevel] = [0x50] };

        var report = GattReportFormatter.BuildReport(device, services, readValues, notifications);

        report.Should().Contain(device.Name)
            .And.Contain(device.Address)
            .And.Contain("Battery Service")
            .And.Contain("Battery Level")
            .And.Contain("读 / 通知")
            .And.Contain("读值")
            .And.Contain("最近通知");
    }
}
