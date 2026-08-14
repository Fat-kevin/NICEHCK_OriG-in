using System.Text;
using YuandaoTws.Domain.Models;

namespace YuandaoTws.Domain;

/// <summary>
/// 协议探测报告格式化（纯逻辑，无 IO）。把 GATT 服务表转成人类可读文本，
/// 用于逆向分析与归档。标准 UUID 名称表仅作展示辅助，不影响协议判定。
/// </summary>
public static class GattReportFormatter
{
    /// <summary>生成完整协议探测报告文本。</summary>
    /// <param name="device">目标设备信息。</param>
    /// <param name="services">枚举出的服务表。</param>
    /// <param name="readValues">特征 UUID → 主动读取到的值（可选）。</param>
    /// <param name="notifications">特征 UUID → 最近收到的一条通知帧（可选）。</param>
    public static string BuildReport(
        HeadsetDevice device,
        IReadOnlyList<GattServiceInfo> services,
        IReadOnlyDictionary<Guid, byte[]>? readValues = null,
        IReadOnlyDictionary<Guid, byte[]>? notifications = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("原道「原点」协议探测报告");
        sb.AppendLine($"设备：{device.Name}    地址：{device.Address}    BLE：{(device.IsLowEnergy ? "是" : "否")}");
        sb.AppendLine($"发现服务数：{services.Count}");
        sb.AppendLine(new string('=', 78));

        for (var i = 0; i < services.Count; i++)
        {
            var service = services[i];
            sb.AppendLine();
            sb.AppendLine($"[服务 {i + 1}] {DescribeUuid(service.Uuid)}");
            if (service.Characteristics.Count == 0)
            {
                sb.AppendLine("    （该服务无特征）");
                continue;
            }

            for (var j = 0; j < service.Characteristics.Count; j++)
            {
                var characteristic = service.Characteristics[j];
                sb.AppendLine($"  [特征 {j + 1}] {DescribeUuid(characteristic.Uuid)}");
                sb.AppendLine($"      属性：{DescribeProperties(characteristic.Properties)}");
                if (characteristic.UserDescription is { Length: > 0 } description)
                {
                    sb.AppendLine($"      描述：{description}");
                }

                if (readValues is not null && readValues.TryGetValue(characteristic.Uuid, out var value))
                {
                    sb.AppendLine($"      读值：{FormatHex(value)}");
                }

                if (notifications is not null && notifications.TryGetValue(characteristic.Uuid, out var notify))
                {
                    sb.AppendLine($"      最近通知：{FormatHex(notify)}");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>输出 UUID 的可读描述：已知标准 UUID 显示名称，未知显示「私有」。</summary>
    public static string DescribeUuid(Guid uuid) =>
        KnownNames.TryGetValue(uuid, out var name)
            ? $"{name} ({uuid})"
            : $"{uuid}（私有）";

    /// <summary>特征属性 → 中文标签，如「读 / 写 / 通知」。</summary>
    public static string DescribeProperties(GattCharacteristicProperties properties)
    {
        var labels = new List<string>(4);
        if (properties.HasFlag(GattCharacteristicProperties.Read))
        {
            labels.Add("读");
        }

        if (properties.HasFlag(GattCharacteristicProperties.Write))
        {
            labels.Add("写");
        }

        if (properties.HasFlag(GattCharacteristicProperties.Notify))
        {
            labels.Add("通知");
        }

        if (properties.HasFlag(GattCharacteristicProperties.Indicate))
        {
            labels.Add("指示");
        }

        return labels.Count == 0 ? "无" : string.Join(" / ", labels);
    }

    /// <summary>字节数组 → hex + ASCII 双栏文本（每行 16 字节）。</summary>
    public static string FormatHex(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "（空）";
        }

        var sb = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            if (offset > 0)
            {
                sb.AppendLine();
            }

            var line = bytes.Skip(offset).Take(16).ToArray();
            var hex = string.Join(" ", line.Select(b => b.ToString("X2")));
            var ascii = new string(line.Select(b => b is >= 0x20 and < 0x7F ? (char)b : '·').ToArray());
            sb.Append($"{hex,-47}  {ascii}");
        }

        return sb.ToString();
    }

    private static readonly IReadOnlyDictionary<Guid, string> KnownNames = new Dictionary<Guid, string>
    {
        // 常用标准服务
        [new Guid("00001800-0000-1000-8000-00805F9B34FB")] = "Generic Access",
        [new Guid("00001801-0000-1000-8000-00805F9B34FB")] = "Generic Attribute",
        [StandardUuids.DeviceInformationService] = "Device Information",
        [StandardUuids.BatteryService] = "Battery Service",
        // 常用标准特征
        [StandardUuids.DeviceName] = "Device Name",
        [new Guid("00002A01-0000-1000-8000-00805F9B34FB")] = "Appearance",
        [StandardUuids.BatteryLevel] = "Battery Level",
        [new Guid("00002A23-0000-1000-8000-00805F9B34FB")] = "System ID",
        [new Guid("00002A24-0000-1000-8000-00805F9B34FB")] = "Model Number",
        [new Guid("00002A25-0000-1000-8000-00805F9B34FB")] = "Serial Number",
        [new Guid("00002A26-0000-1000-8000-00805F9B34FB")] = "Firmware Revision",
        [new Guid("00002A27-0000-1000-8000-00805F9B34FB")] = "Hardware Revision",
        [new Guid("00002A28-0000-1000-8000-00805F9B34FB")] = "Software Revision",
        [new Guid("00002A29-0000-1000-8000-00805F9B34FB")] = "Manufacturer Name",
        [new Guid("00002A05-0000-1000-8000-00805F9B34FB")] = "Service Changed",
    };
}
