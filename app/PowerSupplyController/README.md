# RD6012 Power Desk

基于 .NET 10 / WPF 的电源上位机，提供串口连接、实时读数和曲线、电压/限流设定、输出控制、CSV 记录，以及内置 MCP 接口。

## 运行

```powershell
dotnet build app/PowerSupplyController
dotnet run --project app/PowerSupplyController
# 跳过启动时自动连接串口：
dotnet run --project app/PowerSupplyController -- --offline
```

程序保留现有串口配置。需要时在“串口设置”中调整。

## 界面操作

- 断线或读取失败时，实时读数显示 `--` 和“数据失效”，设备设定、输出与键盘锁控制会暂时禁用；历史曲线和统计仍保留。
- 经典布局和 HTML 布局都会同时显示输出实测电压/电流、设备当前设定电压/限流。输入框及常用预设先修改“待应用”值，只有点击“应用设定”才写入设备；若输出已开启，会先确认。
- 输出使用独立的“开启输出”“关闭输出”按钮。开启前会显示设备当前设定供确认；空格键不再控制输出。
- 电压、电流、功率三条趋势线同步显示在同一张图中。三项数据持续采集，CSV 记录不变。

## AI / MCP

默认监听 `http://127.0.0.1:6012/mcp`，使用 Streamable HTTP 和 Bearer 令牌认证。通过顶部“AI / MCP”面板复制配置、启用控制和设置 AI 限值。初始只读，初始限值 5V / 0.5A。

完整配置和接口说明：[MCP 使用说明](../../docs/MCP使用说明.md)。

## 源码

- `MainWindow.*`：主界面及程序、MCP 生命周期。
- `MainViewModel.cs`：状态、轮询、界面动作及协议映射。
- `ModbusRtuClient.cs`：串口传输及帧校验。
- `Mcp/`：官方 SDK 服务、工具、设备适配和配置存储。
- `McpSettingsWindow.*`：MCP 设置。
- `Models.cs`：公共数据模型。

MCP 复用上位机的连接，不另开串口。寄存器语义以仓库中的厂家文档为准。

## 验证与发布

```powershell
dotnet run --project tests/PowerSupplyController.Mcp.Tests
dotnet publish app/PowerSupplyController -c Release -r win-x64 --self-contained false -o release/PowerSupplyController-mcp-staging
```

自动检查仅操作模拟设备。框架依赖发布版本需要 .NET 10 Desktop Runtime 和 ASP.NET Core Runtime；无运行时机器可用 `--self-contained true` 发布。
