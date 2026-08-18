using System.Reactive.Linq;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using YuandaoTws.Domain;
using YuandaoTws.Domain.Abstractions;
using YuandaoTws.Domain.Models;
using YuandaoTws.Infrastructure.Bluetooth;

const string ControlUuid = "0000a100-1000-8000-4e48-434b4354524c";
const string StatusUuid = "df21fe2c-2515-4fdb-8886-f12c4d67927c";
const string HandsfreeUuid = "0000111e-0000-1000-8000-00805f9b34fb";
var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
Directory.CreateDirectory(outputDirectory);
var outputPath = Path.Combine(outputDirectory, $"charging-confirm-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("原道耳机充电状态确认脚本（逐阶段重连版）");
Console.WriteLine("============================================");
Console.WriteLine("每个阶段都会重新打开所有非免提 RFCOMM 服务，读取连接瞬间的原始帧。");
Console.WriteLine("这个工具只抓包和发送查询，不修改耳机设置。");
Console.WriteLine("请先关闭正式的“原点耳机控制”窗口。");
Console.WriteLine($"记录文件：{outputPath}");
Console.WriteLine();
Console.WriteLine("按 Enter 开始，输入 Q 退出：");
if (string.Equals(Console.ReadLine()?.Trim(), "q", StringComparison.OrdinalIgnoreCase))
{
    return;
}

using var log = new StreamWriter(outputPath, append: false, Encoding.UTF8) { AutoFlush = true };
WriteLog(log, "脚本开始（逐阶段重连版）");

var enumerator = new RfcommServiceEnumerator(NullLogger<RfcommServiceEnumerator>.Instance);
var factory = new SppConnectionFactory(NullLoggerFactory.Instance);
IReadOnlyList<HeadsetDevice> devices;
try
{
    devices = await enumerator.EnumeratePairedDevicesAsync(CancellationToken.None);
}
catch (Exception ex)
{
    PrintError($"枚举设备失败：{ex.Message}");
    WriteLog(log, $"ERROR ENUMERATE {ex}");
    return;
}

var candidates = devices
    .Where(d => d.Name.Contains("YUANDAO", StringComparison.OrdinalIgnoreCase)
        || d.Name.Contains("OriG", StringComparison.OrdinalIgnoreCase))
    .ToList();
if (candidates.Count == 0)
{
    candidates = devices.ToList();
}

if (candidates.Count == 0)
{
    PrintError("没有找到已配对的经典蓝牙设备。请先在 Windows 蓝牙设置中完成配对。");
    WriteLog(log, "没有找到已配对的经典蓝牙设备");
    return;
}

Console.WriteLine("已配对设备：");
for (var i = 0; i < candidates.Count; i++)
{
    Console.WriteLine($"  [{i + 1}] {candidates[i].Name}  {candidates[i].Address}");
}
Console.Write("请选择设备（默认 1）：");
var selection = Console.ReadLine();
var deviceIndex = int.TryParse(selection, out var parsed) && parsed >= 1 && parsed <= candidates.Count ? parsed - 1 : 0;
var device = candidates[deviceIndex];
WriteLog(log, $"设备：{device.Name} / {device.Address} / {device.DeviceId}");

IReadOnlyList<RfcommServiceInfo> services;
try
{
    services = await enumerator.GetServicesAsync(device, CancellationToken.None);
}
catch (Exception ex)
{
    PrintError($"枚举 RFCOMM 服务失败：{ex.Message}");
    WriteLog(log, $"ERROR SERVICES {ex}");
    return;
}

var targetServices = services
    .Where(s => !s.ServiceId.ToString().Equals(HandsfreeUuid, StringComparison.OrdinalIgnoreCase))
    .OrderBy(ServiceRank)
    .ToArray();

Console.WriteLine();
Console.WriteLine("每个阶段将重连以下服务：");
foreach (var service in targetServices)
{
    Console.WriteLine($"  {service.ServiceId}  {service.ServiceName}  [{ServiceRole(service)}]");
}
WriteLog(log, "服务：" + string.Join(" | ", services.Select(s => $"{s.ServiceId} {s.ServiceName}")));

if (targetServices.Length == 0)
{
    PrintError("没有找到可抓取的 RFCOMM 服务，请把日志发给我。");
    return;
}

var phases = new[]
{
    ("A_耳机取出_盒子未插线", "耳机取出盒子，确认充电盒没有插 USB 线"),
    ("B_耳机放入_盒子未插线", "把左右耳机都放回盒子，保持 USB 线未插"),
    ("C_耳机放入_盒子插线", "保持耳机在盒内，给充电盒插上 USB 线，确认盒子正在充电"),
    ("D_耳机放入_拔掉 USB", "保持耳机在盒内，拔掉 USB 线并等待 3 秒"),
    ("E_耳机取出_盒子未插线", "取出左右耳机，保持 USB 线未插"),
};

foreach (var (name, instruction) in phases)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[{name}] {instruction}");
    Console.ResetColor();
    Console.WriteLine("动作完成后按 Enter。脚本随后会重连所有服务并采集 7 秒：");
    Console.ReadLine();

    WriteLog(log, $"== MARK {name}：{instruction} ==");
    await CapturePhaseAsync(device, targetServices, factory, log, name);
    Console.WriteLine("本阶段完成。\n");
}

WriteLog(log, "脚本结束");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"全部完成。请把这个文件发给我：{outputPath}");
Console.ResetColor();
Console.WriteLine("按 Enter 退出。");
Console.ReadLine();

static async Task CapturePhaseAsync(
    HeadsetDevice device,
    IReadOnlyList<RfcommServiceInfo> services,
    ISppConnectionFactory factory,
    StreamWriter log,
    string phase)
{
    var sessions = new List<(RfcommServiceInfo Service, ISppDeviceSession Session, IDisposable Subscription)>();
    var writeLock = new object();
    try
    {
        foreach (var service in services)
        {
            try
            {
                Console.WriteLine($"  正在重连 {service.ServiceId} …");
                var session = await factory.OpenAsync(device, service.ServiceId, CancellationToken.None);
                var yuandaoParser = new YuandaoFrameParser();
                var niceParser = new NiceHckFrameParser();
                var subscription = session.DataReceived.Subscribe(chunk =>
                {
                    lock (writeLock)
                    {
                        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                        var hex = Convert.ToHexString(chunk.Value);
                        WriteLog(log, $"[{timestamp}] PHASE={phase} SERVICE={service.ServiceId} RECV {hex}");

                        foreach (var message in yuandaoParser.Feed(chunk.Value))
                        {
                            var payload = Convert.ToHexString(message.Payload);
                            WriteLog(log, $"[{timestamp}] DECODE SERVICE={service.ServiceId} TYPE=03 ID={message.Id:X2} PAYLOAD={payload}");
                            Console.WriteLine($"    03 id={message.Id:X2} payload={payload}");
                        }

                        foreach (var message in niceParser.Feed(chunk.Value))
                        {
                            var payload = Convert.ToHexString(message.Payload);
                            WriteLog(log, $"[{timestamp}] DECODE SERVICE={service.ServiceId} TYPE=4E OP={message.OpCode:X4} PAYLOAD={payload}");
                            Console.WriteLine($"    4E op={message.OpCode:X4} payload={payload}");
                        }
                    }
                });
                sessions.Add((service, session, subscription));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    打开失败：{ex.Message}");
                WriteLog(log, $"ERROR OPEN PHASE={phase} SERVICE={service.ServiceId} {ex}");
            }
        }

        // 给连接瞬间的主动推送留出时间，然后再发只读查询。
        await Task.Delay(TimeSpan.FromSeconds(2));
        foreach (var (service, session, _) in sessions)
        {
            try
            {
                if (service.ServiceId.ToString().Equals(ControlUuid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteAsync([0x4E, 0x03, 0x00, 0x00, 0x05, 0x00], CancellationToken.None);
                    WriteLog(log, $"[{DateTime.Now:HH:mm:ss.fff}] PHASE={phase} SERVICE={service.ServiceId} SEND 4E0300000500");
                }
                else if (service.ServiceId.ToString().Equals(StatusUuid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteAsync([0x03, 0x03, 0x00, 0x00], CancellationToken.None);
                    WriteLog(log, $"[{DateTime.Now:HH:mm:ss.fff}] PHASE={phase} SERVICE={service.ServiceId} SEND 03030000");
                }
            }
            catch (Exception ex)
            {
                WriteLog(log, $"ERROR SEND PHASE={phase} SERVICE={service.ServiceId} {ex.Message}");
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
    }
    finally
    {
        foreach (var (_, session, subscription) in sessions)
        {
            subscription.Dispose();
            try { await session.DisposeAsync(); }
            catch (Exception ex) { WriteLog(log, $"ERROR CLOSE PHASE={phase} {ex.Message}"); }
        }
    }
}

static int ServiceRank(RfcommServiceInfo service)
{
    var id = service.ServiceId.ToString();
    return id.Equals(ControlUuid, StringComparison.OrdinalIgnoreCase) ? 0
        : id.Equals(StatusUuid, StringComparison.OrdinalIgnoreCase) ? 1
        : id.StartsWith("66666666", StringComparison.OrdinalIgnoreCase) ? 2
        : id.StartsWith("99999999", StringComparison.OrdinalIgnoreCase) ? 3
        : 4;
}

static string ServiceRole(RfcommServiceInfo service)
{
    var id = service.ServiceId.ToString();
    return id.Equals(ControlUuid, StringComparison.OrdinalIgnoreCase) ? "主控 4E"
        : id.Equals(StatusUuid, StringComparison.OrdinalIgnoreCase) ? "状态 03"
        : "候选服务";
}

static void WriteLog(StreamWriter writer, string line)
{
    lock (writer)
    {
        writer.WriteLine(line);
    }
}

static void PrintError(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(message);
    Console.ResetColor();
}
