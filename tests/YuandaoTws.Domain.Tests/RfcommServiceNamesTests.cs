using FluentAssertions;

namespace YuandaoTws.Domain.Tests;

public class RfcommServiceNamesTests
{
    [Fact]
    public void Describe_已知串口服务返回名称与UUID()
    {
        RfcommServiceNames.Describe(new Guid("00001101-0000-1000-8000-00805F9B34FB"))
            .Should().Be("串口 (Serial Port / SPP) (00001101-0000-1000-8000-00805f9b34fb)");
    }

    [Fact]
    public void Describe_未知私有UUID返回短形式并标记私有()
    {
        var privateUuid = new Guid("8E4F12C3-1000-2000-3000-0123456789AB");

        RfcommServiceNames.Describe(privateUuid)
            .Should().Be($"{privateUuid}（私有）");
    }

    [Fact]
    public void Describe_标准基内未知分配号返回短UUID()
    {
        RfcommServiceNames.Describe(new Guid("0000ABCD-0000-1000-8000-00805F9B34FB"))
            .Should().Be("ABCD（私有）");
    }

    [Fact]
    public void SerialPort_常量指向标准串口服务()
    {
        RfcommServiceNames.SerialPort.Should().Be(new Guid("00001101-0000-1000-8000-00805F9B34FB"));
        RfcommServiceNames.Describe(RfcommServiceNames.SerialPort).Should().Contain("串口");
    }

    [Fact]
    public void ShortForm_标准分配号返回四位短码()
    {
        RfcommServiceNames.ShortForm(new Guid("00001101-0000-1000-8000-00805F9B34FB"))
            .Should().Be("1101");
    }

    [Fact]
    public void ShortForm_非标准UUID返回完整字符串()
    {
        var uuid = new Guid("8E4F12C3-1000-2000-3000-0123456789AB");
        RfcommServiceNames.ShortForm(uuid).Should().Be(uuid.ToString());
    }
}
