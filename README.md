# 原点耳机控制

<div align="center">

## 原道「原点」TWS Windows 控制中心

连接耳机、查看左右耳与充电盒状态，并在 Windows 桌面快速控制降噪和音效。

[![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D4?style=flat-square&logo=windows&logoColor=white)](https://www.microsoft.com/windows) [![最新版本](https://img.shields.io/github/v/release/Fat-kevin/NICEHCK_OriG-in?style=flat-square&label=最新版本)](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases/latest) [![下载量](https://img.shields.io/github/downloads/Fat-kevin/NICEHCK_OriG-in/total?style=flat-square&label=下载量)](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases)

</div>

<div align="center">
  <img src="src/YuandaoTws.Desktop/Assets/yuandao-earbuds-model.png" width="520" alt="原道原点耳机" />
</div>

## 功能

| 功能 | 说明 |
| --- | --- |
| 自动连接 | 启动后自动查找已配对的原道耳机，断开后自动重连 |
| 独立电量 | 分别显示左耳、右耳和充电盒电量 |
| 充电状态 | 使用绿色闪电标记当前正在充电的设备；无法确认时显示未知 |
| 降噪模式 | 关闭、通透、普通、深度、试验、风噪六种模式 |
| 均衡器 | 悔恨之泪、均衡中正、欧美澎湃、真律还原、游戏优化、细腻佳音、温婉人声 |
| 快捷开关 | 游戏模式、低延迟、双设备连接、入耳检测、抗风噪 |
| 桌面常驻 | 通知区域显示左右耳电量，鼠标悬停查看精确百分比 |
| 任务栏状态 | 左下角原生状态控件实时显示连接状态和双耳电量 |
| 连接提醒 | 耳机连接后在右下角显示电量、降噪和充电信息 |
| 快速操作 | 托盘右键菜单可打开主界面、重新连接或切换降噪模式 |
| 开机启动 | 可选随 Windows 启动，启动后自动隐藏到通知区域 |
| 原生窗口 | 支持 Windows 毛玻璃/背景采样效果，不依赖浏览器或 WebView |
| 单实例 | 重复启动时自动唤起已有窗口，不会生成多个进程和多个托盘图标 |

## 设备图片

<div align="center">
<table>
<tr>
<td align="center"><img src="src/YuandaoTws.Desktop/Assets/yuandao-earbud-left.png" width="150" alt="原道左耳" /><br />左耳</td>
<td align="center"><img src="src/YuandaoTws.Desktop/Assets/yuandao-earbud-right.png" width="150" alt="原道右耳" /><br />右耳</td>
<td align="center"><img src="src/YuandaoTws.Desktop/Assets/yuandao-charging-case.png" width="220" alt="原道充电盒" /><br />充电盒</td>
</tr>
</table>
</div>

## 下载

前往 [Releases](https://github.com/Fat-kevin/NICEHCK_OriG-in/releases) 下载最新版本。

| 文件 | 适合谁 |
| --- | --- |
| `YuandaoTws-Setup-v0.1.3.exe` | 推荐。完整安装版，支持开始菜单、快捷方式、开机启动和卸载 |
| `YuandaoTws-Desktop-v0.1.3-win-x64-portable.zip` | 便携版，解压后直接运行，不写入安装目录 |
| `SHA256SUMS.txt` | 用于校验下载文件完整性 |

### 安装

1. 在 Windows 蓝牙设置中先完成耳机配对。
2. 下载并运行 `YuandaoTws-Setup-v0.1.3.exe`。
3. 从开始菜单或桌面快捷方式打开“原点耳机控制”。
4. 需要时在右侧设置中打开“开机启动”。

安装程序会在 Windows 的“已安装的应用”中注册卸载入口，开始菜单中也提供“卸载 原点耳机控制”。卸载时会同时移除开机启动项和本软件创建的本地日志目录。

## 使用说明

### 主界面

主界面会集中显示连接状态、固件版本、三路电量、充电标志、降噪模式和均衡器。连接断开后，界面会清除上一轮设备数据并回到“等待连接”。

### 通知区域

程序运行后会在通知区域显示耳机图标：

- 左右两根电池柱代表左右耳电量。
- 绿色闪电表示对应耳机正在充电。
- 鼠标悬停可以查看精确电量。
- 左键打开主界面，右键切换降噪或重新连接。

### 连接提醒

检测到耳机连接后，右下角会出现连接提醒。提醒中会显示左右耳电量、充电盒信息、降噪状态和充电状态。点击提醒可以打开主界面，也可以直接选择降噪模式。

## 系统要求

- Windows 10 或 Windows 11，64 位
- 可用的蓝牙适配器
- 耳机已在 Windows 蓝牙设置中完成配对
- 安装版不需要额外安装 .NET 运行时

## 常见问题

### 为什么显示“等待连接”？

请确认耳机已经在 Windows 蓝牙设置中配对，并打开充电盒靠近电脑。若耳机同时连接了手机，可能需要先暂时断开手机连接。

### 为什么电量显示“—”？

“—”表示设备暂时没有返回该项数据，不代表电量为 0%。保持耳机连接并等待状态刷新即可。

### 为什么 Windows 显示安全提示？

未进行商业代码签名的安装包在首次运行时可能触发 Windows SmartScreen 提示。请从本项目的 GitHub Releases 下载，并在运行前核对文件名和 SHA256 校验值。

### 如何完全退出？

右键点击通知区域图标，选择“退出”。单击窗口右上角只会隐藏到通知区域，不会退出程序。

## 隐私与文件位置

程序只在本机使用 Windows 蓝牙连接和设备状态，不上传耳机数据。运行日志保存在：

```text
%LOCALAPPDATA%\YuandaoTws\logs
```

卸载程序会清理该目录。如需反馈问题，请先删除蓝牙地址等个人信息，再提供日志内容。

## 反馈

欢迎通过 [Issues](https://github.com/Fat-kevin/NICEHCK_OriG-in/issues) 反馈问题。请尽量附上：Windows 版本、耳机固件版本、复现步骤和脱敏后的日志。

## 许可声明

本项目用于个人学习、桌面自动化和经授权的设备控制。耳机协议、商标、图片及相关知识产权归其各自所有者。

<div align="center">

**让耳机控制回到 Windows 桌面。**

</div>
