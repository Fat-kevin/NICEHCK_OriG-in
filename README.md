# 原点耳机控制

<div align="center">

## 原道「原点」TWS Windows 控制中心

在 Windows 桌面上连接、查看和控制原道「原点」降噪耳机。

[![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![最新版本](https://img.shields.io/github/v/release/Fat-kevin/NICEHCK_OriG-in?style=flat-square&label=最新版本)](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases/latest)
[![下载量](https://img.shields.io/github/downloads/Fat-kevin/NICEHCK_OriG-in/total?style=flat-square&label=下载量)](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases)
[![Release 日期](https://img.shields.io/github/release-date/Fat-kevin/NICEHCK_OriG-in?style=flat-square&label=Release%20日期)](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases)
[![x64](https://img.shields.io/badge/发布-x64-555555?style=flat-square)](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases)
[![原生 Windows](https://img.shields.io/badge/应用-原生%20Windows-0078D4?style=flat-square&logo=windows&logoColor=white)](https://github.com/Fat-kevin/NICEHCK_OriG-in)

</div>

<div align="center">
  <img src="src/YuandaoTws.Desktop/Assets/yuandao-earbuds-model.png" width="560" alt="原道原点耳机与充电盒" />
</div>

## 这是什么

原点耳机控制是一款面向 Windows 10/11 的原生桌面工具，专为原道「原点」TWS 耳机设计。

它把耳机的连接状态、电量、降噪和音效控制集中在一个轻量的桌面应用中，不需要浏览器，不依赖 WebView，也不需要额外安装 .NET 运行时。

## 功能一览

### 耳机状态

- 自动查找已配对的原道耳机并连接。
- 断开后自动重连，连接状态始终保持一致。
- 分别显示左耳、右耳和充电盒电量。
- 显示左右耳独立充电状态，充电时使用绿色闪电提示。
- 显示当前固件版本和设备状态。

### 降噪与音效

- 六种降噪模式：关闭、通透、普通、深度、试验、风噪。
- 七种均衡器预设：悔恨之泪、均衡中正、欧美澎湃、真律还原、游戏优化、细腻佳音、温婉人声。
- 游戏模式、低延迟模式、双设备连接、入耳检测和抗风噪。

### Windows 桌面体验

- 原生 Windows 窗口和毛玻璃背景采样效果。
- 浅色、深色主题切换，深色模式支持清晰的层级和文字对比度。
- 右下角连接提醒：显示耳机电量、充电信息和降噪状态。
- 连接提醒可以直接打开控制中心或切换降噪模式。
- 通知区域图标显示左右耳电量，悬停可查看详细状态。
- 任务栏状态胶囊实时显示双耳电量，可在设置中关闭。
- 可选开机启动，启动后自动隐藏到通知区域。
- 单实例运行，重复打开时自动唤起已有窗口。

## 界面与设备展示

<div align="center">
<table>
<tr>
<td align="center"><img src="src/YuandaoTws.Desktop/Assets/yuandao-earbud-left.png" width="150" alt="原道左耳" /><br /><sub>左耳</sub></td>
<td align="center"><img src="src/YuandaoTws.Desktop/Assets/yuandao-earbud-right.png" width="150" alt="原道右耳" /><br /><sub>右耳</sub></td>
<td align="center"><img src="src/YuandaoTws.Desktop/Assets/yuandao-charging-case.png" width="220" alt="原道充电盒" /><br /><sub>充电盒</sub></td>
</tr>
</table>
</div>

主界面重点展示三路电量和当前连接状态，常用的降噪、均衡器与快捷开关按功能分组；透明背景会带入后方应用内容，同时保持文字和控件清晰可读。

## 下载

前往 [GitHub Releases](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases/latest) 下载最新版本。

| 文件 | 推荐场景 |
| --- | --- |
| [`YuandaoTws-Setup-v0.1.7.exe`](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases/download/v0.1.7/YuandaoTws-Setup-v0.1.7.exe) | 推荐。可选择安装位置，支持开始菜单、桌面快捷方式、开机启动和卸载。 |
| `YuandaoTws-Desktop-v0.1.7-win-x64-portable.zip` | 便携版。解压后直接运行，不写入安装目录。 |
| `SHA256SUMS.txt` | 下载文件完整性校验。 |

## 快速开始

1. 打开 Windows **设置 → 蓝牙和设备**，先完成耳机配对。
2. 下载并运行 `YuandaoTws-Setup-v0.1.7.exe`。
3. 安装完成后，从开始菜单或桌面快捷方式打开“原点耳机控制”。
4. 打开充电盒并靠近电脑，应用会自动查找并连接耳机。
5. 在右侧偏好设置中按需开启任务栏状态胶囊、开机启动和深色模式。

安装程序会注册标准卸载入口。可以在 Windows“已安装的应用”中卸载，也可以从开始菜单打开“卸载 原点耳机控制”。

## 日常使用

### 主界面

主界面会集中显示连接状态、固件版本、左右耳电量、充电盒电量、充电状态、降噪模式和均衡器。耳机断开后，旧设备状态会被清除，界面回到“等待连接”。

### 通知区域

程序运行后会显示通知区域图标：

- 左右电池轮廓分别代表左右耳电量。
- 电池颜色会随电量变化，低电量使用警示色。
- 绿色闪电表示对应耳机正在充电。
- 左键打开控制中心，右键可重新连接或快速切换降噪模式。

### 连接提醒

连接成功后，右下角会出现连接提醒。提醒中会显示左右耳电量、充电盒信息、充电状态和当前降噪模式。点击提醒可以打开控制中心，也可以直接选择降噪模式。

## 系统要求

- Windows 10 或 Windows 11，64 位。
- 可用的蓝牙适配器。
- 耳机已在 Windows 蓝牙设置中完成配对。
- 安装版已包含运行所需组件，不需要额外安装 .NET 运行时。

## 常见问题

### 应用显示“等待连接”

请确认耳机已经在 Windows 蓝牙设置中配对，并打开充电盒靠近电脑。如果耳机同时连接了手机，请先暂时断开手机连接再重试。

### 电量显示“—”

“—”表示设备暂时没有返回该项数据，不代表电量为 0%。保持耳机连接并等待状态刷新即可。

### 右下角连接提醒没有显示

连接提醒只在检测到新的连接时出现，并会在一段时间后自动隐藏。重新断开并连接耳机即可再次触发；主界面和通知区域图标不受影响。

### Windows 显示安全提示

未进行商业代码签名的安装包首次运行时可能触发 Windows SmartScreen 提示。请确认文件来自本项目的 GitHub Release，并根据 `SHA256SUMS.txt` 校验文件完整性。

### 如何完全退出应用

右键点击通知区域图标，选择“退出”。单击主窗口右上角只会隐藏到通知区域，不会退出程序。

## 隐私与数据

程序只在本机使用 Windows 蓝牙连接和设备状态，不上传耳机数据，也不会联网同步个人信息。

运行日志保存在：

```text
%LOCALAPPDATA%\YuandaoTws\logs
```

用户设置保存在：

```text
%LOCALAPPDATA%\YuandaoTws\settings.json
```

卸载程序会清理本软件创建的本地日志目录和开机启动项。

## 反馈

欢迎通过 [Issues](https://github.com/Fat-kevin/NICEHCK_OriG-in/issues) 反馈问题。为了便于定位问题，请尽量附上：

- Windows 版本；
- 耳机固件版本；
- 复现步骤；
- 已脱敏的日志内容。

请不要上传蓝牙地址、设备序列号或其他个人信息。

## 许可声明

本项目用于个人学习、桌面自动化和经授权的设备控制。耳机协议、商标、图片及相关知识产权归其各自所有者。

<div align="center">

**让耳机控制回到 Windows 桌面。**

</div>
