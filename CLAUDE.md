# CLAUDE.md

原道「原点」TWS 降噪耳机的 Windows 控制工具（C#/.NET 8 + WPF + WinRT 蓝牙）。

## 快速导航

- **完整交接**：先读 `docs/交接文档.md`（当前状态、代码结构、已知坑、下一步）。
- **设计文档**：`docs/开发文档.md`（架构分层、代码规范、协议逆向方案）。

## 当前状态

- **M1 已完成**：5 层工程、扫描/连接/断开、标准电量（0x2A19）、断线重连、31 项单测全绿。
- **M0 进行中（SPP 方向）**：**GATT 控制通道已证伪**（`fe2c123x` / `77777777` 写入无效果、无状态通知）；确认控制走**经典蓝牙 RFCOMM/SPP**——`df21fe2c` 服务开流成功、连接即推 3 帧基线状态（`03 03 00 03 | 64 64 64` 疑似 L/R/盒电量）、状态变化不主动推 → **命令驱动**；已实现应用内「协议探测」「SPP 探测」「自动试探/状态观察」窗口 + 「一键配对」；协议记录见 `docs/protocol/yuandao-origin.md`。
- **逆向要点**：控制通道是经典蓝牙 SPP 而非 BLE GATT（详见 `docs/交接文档.md` §8 坑 13）；未配对设备写入被 Windows 拒绝（需先配对）；扫描需枚举已配对经典设备 + 按 MAC 探测 BLE 通道。详见 `docs/交接文档.md` §8 坑 9-11。

## 构建 / 测试 / 运行

```bash
dotnet build                          # 0 warning 0 error
dotnet test tests/YuandaoTws.Domain.Tests
dotnet run --project src/YuandaoTws.App
```

Windows 下一键构建+启动：双击根目录 `启动耳机控制台.bat`（杀旧实例 → 构建 → 启动；旧实例运行中会锁 DLL 导致构建复制失败，见 `docs/交接文档.md` 坑 12）。

运行需 Windows + 蓝牙适配器；首次使用请先在 Windows 蓝牙设置中与耳机配对。

## 分层红线（不可破坏）

```
Presentation → Application → Domain ← Infrastructure
```

- Domain 纯 C#，定义接口；Infrastructure 实现接口并承载所有 `Windows.*`（WinRT）调用。
- **WinRT 类型禁止传出 Infrastructure**；跨层只传领域模型（`HeadsetDevice`、`BatteryInfo` 等）。
- 品牌协议全部隔离在 `IDeviceProtocol` 适配器内，逆向后只需改 `YuandaoProtocol` 一个类。

## 已知坑（改代码前必看）

1. WPF 引用 WinRT 需 TFM `net8.0-windows10.0.19041.0`（否则 CS0234）。
2. `YuandaoTws.App` 命名空间内 `Application` 会被解析成 Application 层命名空间 → **用 `System.Windows.Application` 全限定**。
3. `GattSession` 在 `Windows.Devices.Bluetooth.GenericAttributeProfile` 命名空间。
4. 异步取消用 `.AsTask(cancellationToken)`；GATT 操作设超时。

## 代码规范

命名、异步（禁 `.Result`/`.Wait()`）、领域异常体系、单测要求，详见 `docs/开发文档.md` 第 5 章。
