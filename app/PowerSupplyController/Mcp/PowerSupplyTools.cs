using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerSupplyController.Mcp;

[McpServerToolType]
public sealed class PowerSupplyTools(PowerSupplyService service)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "power_supply_get_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("查询电源连接状态、AI 控制开关、用户配置的电压(V)/电流(A)限值。写入前必须查询；限值不是自动检测的设备额定值。")]
    public async Task<CallToolResult> GetCapabilities(CancellationToken cancellationToken) =>
        Result(await service.GetCapabilitiesAsync(cancellationToken));

    [McpServerTool(Name = "power_supply_get_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("实时读取电源设定、测量值、输出及保护状态，包含单位和采样时间；读取失败不返回旧缓存。不会打开串口或改变电源输出。")]
    public async Task<CallToolResult> GetStatus(CancellationToken cancellationToken) =>
        Result(await service.GetStatusAsync(cancellationToken));

    [McpServerTool(Name = "power_supply_set_setpoints", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("同时设置电压(V)和限流(A)，不改变输出开关。输出已经开启时立即影响负载。需启用 AI 控制；先查询能力与状态。写入后回读确认，结果不确定时只读取状态，不自动重试写入。")]
    public async Task<CallToolResult> SetSetpoints(
        [Description("电压，单位 V，非负、最多三位小数，不超过用户配置的 AI 限值。例如 5.0。")] double voltage_v,
        [Description("限流值，单位 A，非负、最多三位小数，不超过用户配置的 AI 限值。例如 0.5。")] double current_a,
        CancellationToken cancellationToken) => Result(await service.SetSetpointsAsync(voltage_v, current_a, cancellationToken));

    [McpServerTool(Name = "power_supply_set_output", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("明确开启或关闭电源输出，需启用 AI 控制。开启前检查设备设定在 AI 限值内且无保护。关闭不受现有设定限值约束。软件关闭依赖串口通信，不是硬件急停。结果不确定时只读取状态，不自动重试写入。")]
    public async Task<CallToolResult> SetOutput(
        [Description("true 开启输出，false 关闭输出；不是切换开关。")] bool enabled,
        CancellationToken cancellationToken) => Result(await service.SetOutputAsync(enabled, cancellationToken));

    private static CallToolResult Result(McpReply reply) => new()
    {
        IsError = !reply.Success,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(reply, JsonOptions) }],
        StructuredContent = JsonSerializer.SerializeToElement(reply, JsonOptions)
    };
}
