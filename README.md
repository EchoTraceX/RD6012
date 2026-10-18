# RD6012 Power Desk

RD6012 可编程直流电源的 Windows 上位机。项目通过真实 Modbus RTU 协议读取和控制设备，并复用同一套后端同时提供：

- 新版 **C# WPF + WebView2 + HTML/CSS/JS** 界面
- 原版 **纯 WPF** 应用（作为后端来源和参考实现）
- 本机 **AI / MCP** 控制接口

![RD6012 Power Desk 主界面](app/RD6012.PowerDesk/preview/actual-window.png)

## 新版本特性

- **四个主视图**：工作台 / 趋势 / 预设 / 设备，全部为标签页切换
- **实时遥测**：电压、电流、功率、输入电压、温度、CV/CC 模式、保护、键盘锁
- **输出与设定**：显式开启/关闭输出；上电和输出开启期间的写入使用应用内自绘确认弹窗
- **趋势与记录**：60 秒 / 5 分钟 / 15 分钟趋势，暂停、清空，CSV 记录
- **预设库**：12 个常用负载预设；载入只修改待应用值，不直接写设备
- **串口设置**：端口、波特率、地址、数据位、校验位、停止位、端口刷新
- **AI / MCP 控制台**：服务启停、权限、限幅、令牌、真实访问审计
- **桌面体验**：圆角无边框窗口、自绘窗口控制、窗口位置记忆、DPI 适配、暗色/浅色主题

上电确认弹窗：

![上电确认](app/RD6012.PowerDesk/preview/actual-confirm.png)

## 技术架构

```text
┌─────────────────────────────────────────────┐
│ C# WPF Shell                                 │
│  MainWindow.xaml / MainWindow.xaml.cs         │
│  - 窗口区域、拖拽、最大化/还原、DPI           │
│  - Modbus / MCP / 记录命令                   │
│  - WebView2 JSON 消息桥                      │
└──────────────────────┬──────────────────────┘
                       │ postMessage / snapshot
┌──────────────────────▼──────────────────────┐
│ Web UI: app/RD6012.PowerDesk/web/index.html  │
│  HTML / CSS / JavaScript                    │
│  工作台 · 趋势 · 预设 · 设备 · MCP 控制台      │
└──────────────────────┬──────────────────────┘
                       │ 复用后端源码
┌──────────────────────▼──────────────────────┐
│ app/PowerSupplyController                    │
│  ModbusRtuClient · MainViewModel             │
│  Mcp/EmbeddedMcpServer · 采样 / CSV 记录      │
└─────────────────────────────────────────────┘
```

新版通过链接源码复用旧版后端，不修改旧版应用，也不改变现有串口/MCP 配置文件位置。

## 快速开始

### 环境

- Windows 10/11 x64
- .NET 10 SDK（构建）或 Desktop Runtime + ASP.NET Core Runtime 10（运行）
- Microsoft Edge WebView2 Evergreen Runtime

### 构建和运行

```powershell
dotnet build app/RD6012.PowerDesk -c Release
dotnet run --project app/RD6012.PowerDesk
```

框架依赖发布：

```powershell
dotnet publish app/RD6012.PowerDesk -c Release -r win-x64 `
  --self-contained false -p:PublishReadyToRun=false `
  -p:DebugType=None -p:DebugSymbols=False `
  -o release/RD6012.PowerDesk
```

预编译包可在 [Releases](https://github.com/EchoTraceX/RD6012/releases) 下载（若尚未发布，按上面的命令构建）。

### 旧版 WPF 应用

```powershell
dotnet build app/PowerSupplyController -c Release
dotnet run --project app/PowerSupplyController
```

旧版依赖 LiveChartsCore/SkiaSharp；新版界面不再使用这些依赖，但仍保留旧项目源码。

## MCP

端点：`http://127.0.0.1:6012/mcp`  
传输：Streamable HTTP + Bearer Token

工具：

- `power_supply_get_capabilities`
- `power_supply_get_status`
- `power_supply_set_setpoints`
- `power_supply_set_output`

默认只读；允许控制需要在应用内显式打开并设置电压/电流限幅。写入需要回读确认，不自动重试。MCP 只监听 `127.0.0.1`。

- [MCP 使用说明](docs/MCP使用说明.md)
- [MCP 实机测试记录](docs/MCP实机测试记录-20260927.md)

## 安全说明

- “关闭输出”是软件停止，**不是硬件急停**。请保留物理断电手段。
- AI/MCP 写入受用户限幅约束，但软件限幅不能替代现场安全检查。
- 修改 MCP 端口或令牌后，需要同步修改 AI 客户端配置。
- 真机首次验证建议在空载或安全负载下进行。

## 协议

RD6012 使用 Modbus RTU（115200 8N1，默认地址 1），关键寄存器：

| 功能 | 地址 | 功能码 |
| --- | --- | --- |
| 读取状态 | `0x0000` | `0x03` |
| 设置电压/限流 | `0x0008` | `0x10` |
| 输出开关 | `0x0012` | `0x06` |
| 键盘锁 | `0x0013` | `0x06` |

电压值 ×100、电流值 ×1000、功率值 ×100。完整寄存器说明见厂家协议文档：

[docs/RD6006和RK6006_Modbus通信协议寄存器_20250508.docx](docs/RD6006和RK6006_Modbus通信协议寄存器_20250508.docx)

## 目录

```text
.
├── app/
│   ├── RD6012.PowerDesk/       # 新版 WPF + WebView2 上位机
│   └── PowerSupplyController/ # 原版 WPF 应用 / 后端来源
├── docs/                       # MCP、协议和设计文档
├── prototype/                  # HTML 原型与截图
├── scripts/                    # MCP 测试脚本
└── tests/                      # MCP 假设备测试项目
```

## 验证

已在真实 RD6012 上验证：

- COM 串口实时读取电压、电流、功率、输入电压、温度、CV 模式、保护、键盘锁
- MCP 初始化与 `power_supply_get_capabilities` / `power_supply_get_status` 真实调用
- 数据记录开始/停止和 CSV 内容
- 四个标签页、设备信息、端口刷新
- 圆角窗口、最大化/还原、拖拽、任务栏避让
- 上电确认弹窗的取消/Esc，不触发实际输出

写入类操作（设定、输出开关）没有作为自动化测试的一部分执行；请在实际使用时自行确认负载安全。

## 使用声明

本项目主要用于个人学习和设备控制。仓库目前未附开源许可证。
