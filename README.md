# NICEHCK OriG-in

<div align="center">

**Windows 上的原道 OriG in「原点」耳机控制中心**

轻盈、安静、随时可用的 ANC / EQ / 电量控制工具。

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![Bluetooth](https://img.shields.io/badge/Bluetooth-RFCOMM%20%2F%20SPP-0A84FF?style=flat-square&logo=bluetooth&logoColor=white)](https://www.bluetooth.com/)

</div>

---

## 这是什么

`NICEHCK OriG-in` 是一个面向 Windows 的原道 OriG in「原点」TWS 耳机控制工具。

它不改变系统音频链路，只通过耳机厂商的经典蓝牙控制通道读取和修改设备状态，让你在 Windows 桌面上直接完成：

- 查看左耳、右耳和充电盒独立电量
- 查看左右耳充电状态
- 切换降噪、通透与风噪抑制模式
- 切换七档 EQ
- 快速控制游戏模式、低延迟、双设备和入耳检测
- 启动后自动扫描并连接已配对的原道 OriG in；断开后自动重新查找
- 中文 Fluent 风格生产界面；托盘入口保留为常驻控制入口

项目同时保留了协议探测能力，方便后续维护和兼容性验证；日常使用推荐运行新的生产版桌面界面。

## 桌面界面

当前生产版使用 **WPF + Windows Composition Host Backdrop + Win2D GPU Blur** 的轻量 Windows Fluent 风格：

- 中文界面，遵循 Windows 桌面控件语义；
- 使用原生 Host Backdrop 采样其它应用内容，并由 Win2D/Direct2D 在 GPU 上做高斯模糊；不采用 CPU 截图、浏览器套壳或 WebView；
- 日常状态通过原生 `Button`、`RadioButton`、`ToggleButton`、`ProgressBar` 和 `ScrollViewer` 展示；
- 以设备状态、降噪、均衡器和偏好设置为中心，不使用宣传页、仪表盘或多层伪玻璃卡片；
- 软件启动和连接丢失时自动查找名称匹配 `YUANDAO` / `OriG` 的已配对耳机并尝试建立 SPP 控制会话。

> `YuandaoTws.Desktop` 是持续演进中的生产界面；`YuandaoTws.App` 是保留的调试/协议探测工具。

## 功能一览

| 模块 | 支持内容 |
| --- | --- |
| 设备连接 | 启动/断开后自动查找名称匹配 `YUANDAO` / `OriG` 的已配对耳机；也可在生产界面手动扫描并选择设备；使用 RFCOMM/SPP 连接并自动重连 |
| 电量 | 左耳 / 右耳 / 充电盒；充电状态；未知值不伪装成 0% |
| ANC | 关闭、通透、普通降噪、深度降噪、试验、风噪抑制 |
| EQ | 悔恨之泪、均衡中正、欧美澎湃、真律还原、游戏优化、细腻佳音、温婉人声 |
| 快捷开关 | 游戏模式、低延迟、双设备连接、入耳检测、抗风噪 |
| 桌面体验 | WPF + Windows Composition Host Backdrop/Win2D GPU Blur 中文 Fluent 界面、无额外外框的原生窗口、自绘标题栏、托盘 ANC 快捷切换、任务栏电池进度、单文件 EXE |
| 工程工具 | GATT/SPP 探测、协议验证、会话记录（调试版保留） |

## 安装与运行

### 环境

- Windows 10/11 x64
- .NET 8 SDK（开发构建需要）
- 可用的蓝牙适配器
- 已在 Windows 蓝牙设置中与耳机完成配对

### 开发运行

```powershell
dotnet restore
dotnet build
dotnet run --project src/YuandaoTws.Desktop
```

生产版入口：

```text
src/YuandaoTws.Desktop
```

调试/协议工具入口：

```text
src/YuandaoTws.App
```

### 发布单文件

```powershell
dotnet publish src/YuandaoTws.Desktop `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:DebugType=None
```

> `DebugType=None` 跳过 PDB 调试符号；WPF 原生库已通过 csproj 的 `IncludeNativeLibrariesForSelfExtract` 嵌入 exe，产出即单个绿色 EXE。

首次使用前，请先打开 Windows 设置 → 蓝牙和设备，与耳机配对。非打包桌面应用对未配对设备的写入操作可能会被系统拒绝。

## 技术架构

```text
┌──────────────────────────────────────────────┐
│ YuandaoTws.Desktop / YuandaoTws.App          │
│ WPF 生产界面 / 调试与协议探测界面             │
└───────────────────────┬──────────────────────┘
                        │
┌───────────────────────▼──────────────────────┐
│ YuandaoTws.Application                       │
│ 连接编排、状态归约、命令串行化、写后回查      │
└───────────────────────┬──────────────────────┘
                        │
┌───────────────────────▼──────────────────────┐
│ YuandaoTws.Domain                            │
│ 领域模型、协议契约、NiceHCK/BES 帧解析         │
└───────────────────────▲──────────────────────┘
                        │
┌───────────────────────┴──────────────────────┐
│ YuandaoTws.Infrastructure                    │
│ Windows Bluetooth / RFCOMM / SPP / WinRT      │
└──────────────────────────────────────────────┘
```

核心控制链路是经典蓝牙 RFCOMM/SPP，而不是 BLE GATT：

```text
0000a100-1000-8000-4e48-434b4354524c
```

协议采用 NiceHCK/BES `0x4E` 帧格式。设置操作统一遵循：

```text
发送设置 → 等待设备处理 → 查询回读 → 以设备返回值确认 UI
```

这意味着界面不会在命令刚发出时就假设成功。

## 项目结构

```text
src/
├── YuandaoTws.Domain/          纯领域层与协议解析
├── YuandaoTws.Application/     连接、状态、控制服务
├── YuandaoTws.Infrastructure/  WinRT 蓝牙与 SPP 实现
├── YuandaoTws.App/             调试/协议探测界面
└── YuandaoTws.Desktop/         中文 Fluent 生产版界面（WPF + Windows Composition）

tests/
└── YuandaoTws.Domain.Tests/    协议与领域单元测试

docs/
├── 开发文档.md
├── 交接文档.md
└── protocol/                   协议逆向与验证记录
```

## 开发约束

- Domain 不引用 WPF、WinRT 或 Infrastructure
- WinRT 类型只允许出现在 Infrastructure
- Application 不依赖具体 UI
- 所有蓝牙 IO 使用 `async/await`
- 禁止 `.Result` / `.Wait()`
- 控制命令必须经过写入后回查
- 协议解析必须用真实帧和异常帧测试

## 已知限制

- 目前主要面向原道 OriG in「原点」及同协议族设备
- 当前生产版采用 WPF + 原生 Windows Composition Host Backdrop/Win2D GPU Blur（Win10 1703+）；不可用时回退到系统 Acrylic，不使用 WinUI 3、WebView 或浏览器套壳
- codec 切换属于实验能力，可能导致短暂断音或重新连接
- 充电盒返回 0 时代表未知，不代表真实电量为 0%
- 使用前必须完成 Windows 蓝牙配对

## 文档

- [开发文档](docs/开发文档.md)
- [项目交接文档](docs/交接文档.md)
- [NiceHCK/BES 协议记录](docs/protocol/nicehck-bes-protocol.md)
- [原道设备协议记录](docs/protocol/yuandao-origin.md)

## 许可

本项目仅用于个人学习、桌面自动化和经授权的设备控制。耳机协议、商标和相关知识产权归其各自所有者。

<div align="center">

**让耳机控制回到桌面。**

</div>
