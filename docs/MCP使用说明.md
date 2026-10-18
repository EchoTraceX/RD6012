# RD6012 上位机内置 MCP 使用说明

## 第一次使用

1. 启动 `release/PowerSupplyController/PowerSupplyController.exe`，通过“串口设置”连接电源。
2. 点击顶部 **AI / MCP · 只读**，确认面板显示“MCP 运行中”。
3. 点击“复制客户端 JSON”，或分别复制地址和令牌，配置 AI 客户端。
4. 先让 AI 查询能力和当前状态。
5. 需要控制时，按实际负载设置最高电压和最大限流，勾选“允许 AI 设置参数及开启 / 关闭输出”，点击“保存并应用”。

默认地址为 `http://127.0.0.1:6012/mcp`，服务随上位机启动，初始**只读**，AI 限值为 **5V / 0.5A**。限值可配置为大于 0～60V、大于 0～12A，它们是用户配置上限，不是自动识别的设备额定值。请按实际电源和负载设置。

AI 与界面共用上位机已有串口连接，AI 不应另开 COM 口。软件关闭后接口停止。关闭 MCP 或撤销 AI 控制权限**不会自动关闭电源输出**。

保存后设置立即生效；修改端口或令牌会重启服务，客户端需要更新配置并重新连接。复制按钮复制的是**已保存**的配置，修改或生成令牌后请先保存再复制。只建议运行一个上位机实例，避免串口和端口占用。

## 客户端接入

服务使用标准 MCP **Streamable HTTP**、无状态传输、官方 C# SDK `ModelContextProtocol.AspNetCore 2.2.0`。每次请求都需要 `Authorization: Bearer <令牌>`。

本版本供同一台 Windows 电脑上的客户端访问，不提供公网服务、OAuth、旧式 `/sse` 或 stdio 启动方式。云端 AI 无法直接访问你的 `127.0.0.1`；只支持 stdio 的客户端需要另外配置兼容桥接程序。

### 通用 JSON 配置

以下是常见格式，具体外层字段以客户端为准。面板复制功能会填入真实地址和令牌。

```json
{
  "mcpServers": {
    "rd6012": {
      "type": "http",
      "url": "http://127.0.0.1:6012/mcp",
      "headers": { "Authorization": "Bearer 替换为上位机复制的令牌" }
    }
  }
}
```

若用表单添加服务，选择 Streamable HTTP，填写 URL 和 Authorization 请求头即可。连接的是已打开的上位机，无需配置 EXE 启动命令。不要把含真实令牌的配置提交到仓库。

### Codex

在 `~/.codex/config.toml`（Windows 通常为 `%USERPROFILE%\.codex\config.toml`）中合并以下内容，不要覆盖已有配置：

```toml
[mcp_servers.rd6012]
url = "http://127.0.0.1:6012/mcp"
http_headers = { Authorization = "Bearer 替换为上位机复制的令牌" }
```

也可将 `http_headers` 行替换为 `bearer_token_env_var = "RD6012_MCP_TOKEN"`，并让启动 Codex 的进程能读取该环境变量。修改环境后重新启动客户端。本次开发没有自动修改你的 Codex 配置。

配置字段依据：[Codex 官方 MCP 文档](https://developers.openai.com/codex/mcp)。

### Grok CLI、Claude Code 与 ZCode

这三种本地客户端都可连接上位机的 Streamable HTTP MCP，并为请求附加 `Authorization: Bearer <令牌>`。在本机配置时，将令牌保存在各自的用户配置文件中，不要写入项目仓库：

| 客户端 | 用户配置位置 | MCP 条目 |
|---|---|---|
| Grok CLI | `%USERPROFILE%\.grok\config.toml` | `[mcp_servers.rd6012]`，填写 `url` 和 `headers.Authorization` |
| Claude Code | `%USERPROFILE%\.claude.json` | 顶层 `mcpServers.rd6012`，`type` 为 `http` |
| ZCode | `%USERPROFILE%\.zcode\cli\config.json` | `mcp.servers.rd6012`，`type` 为 `http` |

配置后使用客户端新会话（必要时重启客户端）加载 MCP；上位机也需要保持打开。若通过其他支持 MCP 的本地客户端使用 DeepSeek 模型，应配置该客户端，而不是直接配置模型本身。

### 对 AI 说什么

- “查询电源的 AI 控制权限和限值，再读取当前状态。”
- “电压设为 3.3V，限流设为 0.2A，保持当前输出开关状态，报告回读结果。”
- “确认设备设置在允许范围内后开启输出，再读取实际电压、电流。”
- “关闭电源输出并读取状态确认。”

## 工具

| 工具 | 参数 | 行为 |
|---|---|---|
| `power_supply_get_capabilities` | 无 | 查询连接状态、型号代码（连接时）、控制权限、用户限值和确认规则；不访问串口 |
| `power_supply_get_status` | 无 | 从电源实时读取状态；失败不返回旧缓存 |
| `power_supply_set_setpoints` | `voltage_v: number`、`current_a: number` | 两者必填，同时设置电压和限流，不改变输出开关 |
| `power_supply_set_output` | `enabled: boolean` | true 开启、false 关闭，是明确设定，不是切换 |

电压单位 V、电流单位 A，必须为有限、非负数，最多三位小数且不超过 AI 限值。接受三位小数不代表所有型号具有相同物理精度，以设备回读为准。**输出已经开启时，设置电压/限流会立即影响负载。**

设置前检查写权限、连接、参数、当前保护状态。开启前还会读取设备当前设定，确保均在 AI 限值内。关闭输出仍需写权限和已有串口连接，但不要求成功预读，也不受现有设定越界或保护状态影响。

## 返回值与失败处理

工具同时返回文本 JSON 和 `structuredContent`，内容一致，失败时 MCP `isError=true`。

```json
{
  "success": true,
  "code": "ok",
  "message": "设备寄存器已回读确认；不表示输出电压已稳定。",
  "writeAttempted": true,
  "writeAcknowledged": true,
  "confirmed": true,
  "data": {
    "connected": true,
    "sampledAt": "2026-09-27T12:00:00+08:00",
    "productModelCode": 6012,
    "serialNumber": 12345,
    "firmwareVersion": 140,
    "voltageSetV": 5,
    "currentLimitA": 0.5,
    "voltageOutV": 0,
    "currentOutA": 0,
    "powerOutW": 0,
    "voltageInV": 24,
    "temperatureC": 25,
    "outputEnabled": false,
    "protectionCode": 0,
    "protection": "正常",
    "modeCode": 0,
    "mode": "CV 恒压",
    "keyboardLocked": false
  }
}
```

以上是示例，不是实际读数。`sampledAt` 为电脑采样时间及本地时区偏移。

- `writeAttempted`：调用过写方法，不能据此判定设备是否执行。
- `writeAcknowledged`：收到经驱动校验的有效写响应。
- `confirmed`：收到写响应且随后回读与目标一致，**不代表负载端电压已经稳定**。
- 只读工具的 `confirmed=false`，读取是否成功看 `success`。

| code | 处理方式 |
|---|---|
| `not_connected` | 在上位机连接电源；AI 不自动扫描或打开串口 |
| `control_disabled` | 用户在 MCP 面板启用控制并保存 |
| `invalid_setpoints` | 检查有限数值、非负、小数位及当前限值 |
| `limit_exceeded` | 开启前发现设备设定越界，先调整设定 |
| `protection_active` | 检查电源保护原因，接口不会清除保护 |
| `read_failed` | 读取失败；若发生在预检查阶段，尚未发送写入 |
| `write_uncertain` | 写响应未确认，可能已执行；先读取，不自动重试写入 |
| `readback_failed` | 已收到写确认，但回读失败；先读取，不自动重试写入 |
| `readback_mismatch` | 回读与请求不符，检查返回状态，不自动重试写入 |
| `busy` | 另一个 AI 操作正在处理，稍后重试读取，写入前重新读取 |
| `server_stopping` | 服务正在停止或已关闭，先检查上位机 |

客户端超时、取消或断连不保证已开始的设备指令被撤销，之后应先读取状态。一条工具调用内检查、写入、回读连续执行；多条调用之间没有独占租约，界面或其他 AI 可以操作。

## 只读连接检查

项目和发布包都附带 `scripts/Test-Mcp.ps1`，默认只做握手、发现工具和能力查询，不发送写入：

```powershell
$env:RD6012_MCP_TOKEN = '从上位机复制的令牌'
.\scripts\Test-Mcp.ps1
# 已连接电源时，额外读取实际状态：
.\scripts\Test-Mcp.ps1 -ReadStatus
# 修改过端口时：
.\scripts\Test-Mcp.ps1 -Endpoint 'http://127.0.0.1:16012/mcp'
```

无硬件检查界面可运行 `PowerSupplyController.exe --offline`，它只跳过启动时自动连接/扫描串口；之后仍可通过串口设置连接真实设备，不是模拟设备模式。

## 配置、日志和边界

- 配置：`%APPDATA%\RD6012_PowerSupplyController\mcp_settings.json`。
- 令牌：256 位随机数，用 Windows DPAPI 当前用户范围加密保存。不能直接复制配置给另一个 Windows 用户。文件损坏或无法解密时服务不启动，从界面重新保存有效设置后更新客户端。
- 日志：同目录 `diagnostic.log`，记录时间、进程、操作参数、结果和确认标记，不记录令牌。
- 只监听 IPv4 `127.0.0.1`；Host 只接受正确端口的 `127.0.0.1` 或 `localhost`，有 Origin 时必须同源，不开启跨站 CORS。建议固定使用 `127.0.0.1` 避免 IPv6 差异。
- 请求体上限 16 KiB，最多 16 条并发连接；同一时间仅接受一个 AI 设备操作，其他返回 `busy`。
- 所有持有令牌的 AI 共用权限和限值，本版不区分客户端身份。
- 软件关闭输出依赖串口和设备响应，不能替代硬件急停；撤销访问不回滚已执行指令。

## 排查

| 现象 | 处理 |
|---|---|
| MCP 未运行 / 端口占用 | 关闭多余实例或改用空闲端口；错误不会阻止原有串口界面使用 |
| 浏览器打开不是网页 | 这是 MCP 协议端点，需要握手和认证，使用客户端或检查脚本 |
| HTTP 401 | 重新复制已保存令牌，检查 `Bearer ` 前缀；轮换后旧令牌失效 |
| HTTP 403 | 使用本机 URL，不从任意网页跨域访问 |
| 能发现工具但无法写 | 工具始终可发现，执行时才检查权限，启用控制后保存 |
| 撤销控制后仍有输出 | 撤销权限不改变设备输出，请在界面关闭输出 |
| 只有 command/args 配置 | 客户端可能仅支持 stdio，请换用支持 HTTP 和认证头的接入方式 |

## 开发和验证

```powershell
dotnet build app/PowerSupplyController
dotnet run --project tests/PowerSupplyController.Mcp.Tests
dotnet publish app/PowerSupplyController -c Release -r win-x64 --self-contained false -o release/PowerSupplyController-mcp-staging
```

框架依赖发布版需要 .NET 10 Desktop Runtime 和 ASP.NET Core Runtime，当前开发电脑均已安装。无运行时的电脑可以用 `--self-contained true` 重新发布。

`Mcp/` 内包含服务、工具、设备适配和配置存储；`McpSettingsWindow.*` 为设置界面；`MainWindow` 管理生命周期。整个设备操作通过 WPF Dispatcher，与现有轮询和按钮共用 UI 线程及 Modbus 客户端锁。

沿用现有状态读取 `0x0000`、配对设定 `0x0008 / 0x0009`、输出 `0x0012` 及驱动换算。没有开放原始寄存器写入，也没有改变串口协议。寄存器语义以仓库中的厂家文档为准。

自动检查使用内存模拟设备、真实 HTTP 监听和官方 MCP 客户端，覆盖权限、输入、保护、回读不确定性、取消、并发、认证、Host/Origin、握手、工具发现、端口冲突和重启。另通过真实 WPF `--offline` 进程检查入口、设置窗口和只读连接。

**2026-09-27 已完成用户授权的真实电源空载测试。** 经 MCP 设置 3.3V / 0.1A、5V / 0.1A，分别开启输出，等待读数稳定并连续采样三次，随后关闭输出；设定及开关均回读确认。61V 越界请求被拒绝且未调用写入。结束后恢复原设定 5V / 6.546A，输出关闭、回读 0V / 0A，MCP 权限和令牌恢复测试前配置。详细记录见 [MCP 实机测试记录](MCP实机测试记录-20260927.md)。本次未验证带载性能、电流限流动作、硬件保护触发或外接仪表精度。

实测开启和关闭后，设备返回的电压读数会经历短暂过渡。因此 `confirmed=true` 只确认寄存器设定/开关，不应当作电压已经稳定。需要稳定电压的流程，应再调用状态工具并等待连续读数满足目标。

另附 `tests/PowerSupplyController.Mcp.Tests/evaluation.xml` 的 10 道只读 AI 理解题。使用 `dotnet run --project tests/PowerSupplyController.Mcp.Tests -- --evaluation-server` 启动固定模拟设备，地址为 `http://127.0.0.1:16012/mcp`，测试令牌是 64 个数字 `0`。此令牌仅用于模拟器，真实上位机绝不使用它。题目答案来自固定测试数据；没有声称已进行真实 AI 客户端的自然语言评测。结束模拟服务按 Ctrl+C。

参考：[MCP Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)、[官方 C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)。
