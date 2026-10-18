using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerSupplyController.Mcp;

// All device access, including read/modify/write and confirmation, runs as one UI dispatcher operation.
public interface IPowerSupplyDevice
{
    bool IsConnected { get; }
    PowerSupplyData Snapshot { get; }
    bool Refresh();
    bool WriteSetpoints(double voltage, double current);
    bool WriteOutput(bool enabled);
}

public sealed record McpReply(
    bool Success, string Code, string Message, bool WriteAttempted = false,
    bool WriteAcknowledged = false, bool Confirmed = false, JsonNode? Data = null);

public sealed class PowerSupplyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPowerSupplyDevice _device;
    private readonly Func<Func<McpReply>, CancellationToken, Task<McpReply>> _dispatch;
    private readonly Action<string> _audit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpSettings _settings;
    private volatile bool _active;

    public PowerSupplyService(IPowerSupplyDevice device, McpSettings settings,
        Func<Func<McpReply>, CancellationToken, Task<McpReply>> dispatch, Action<string> audit)
    {
        _device = device;
        _settings = settings;
        _dispatch = dispatch;
        _audit = audit;
    }

    public McpSettings Settings => Volatile.Read(ref _settings);
    public void Configure(McpSettings settings) => Volatile.Write(ref _settings, settings);
    public void SetActive(bool active) => _active = active;

    public Task<McpReply> GetCapabilitiesAsync(CancellationToken cancellationToken) => ExecuteAsync("get_capabilities", () =>
    {
        var settings = Settings;
        return new(true, "ok", "AI 限值由用户配置，不代表自动识别的设备额定值。", Data: JsonSerializer.SerializeToNode(new
        {
            connected = _device.IsConnected,
            deviceModelCode = _device.IsConnected ? (int?)_device.Snapshot.ProductModelCode : null,
            allowControl = settings.AllowControl,
            maxVoltageV = settings.MaxVoltage,
            maxCurrentA = settings.MaxCurrent,
            minVoltageV = 0,
            minCurrentA = 0,
            parameterDecimalPlaces = 3,
            limitSource = "user_configured",
            transport = "streamable-http",
            concurrentRequests = "busy_if_another_device_operation_is_pending",
            confirmation = "register_readback_only_not_physical_voltage_settling",
            softwareStopOnly = true
        }, JsonOptions));
    }, cancellationToken);

    public Task<McpReply> GetStatusAsync(CancellationToken cancellationToken) => ExecuteAsync("get_status", () =>
    {
        if (!_device.IsConnected) return Error("not_connected", "请先在上位机串口设置中连接电源。");
        if (!_device.Refresh()) return Error("read_failed", "读取设备失败；没有返回缓存值，请检查串口连接。");
        return new(true, "ok", "已读取设备当前状态。", Data: State());
    }, cancellationToken);

    public Task<McpReply> SetSetpointsAsync(double voltage, double current, CancellationToken cancellationToken) =>
        ExecuteAsync($"set_setpoints voltageV={voltage} currentA={current}", () =>
        {
            var rejection = CheckControl();
            if (rejection is not null) return rejection;
            if (!ValidSetpoint(voltage, Settings.MaxVoltage) || !ValidSetpoint(current, Settings.MaxCurrent))
                return Error("invalid_setpoints", "电压和电流必须是有限、非负数，最多三位小数，且不超过 get_capabilities 返回的 AI 限值。");
            if (!_device.Refresh()) return Error("read_failed", "写入前读取失败，没有发送设置指令。");
            if (_device.Snapshot.Protect != 0) return Error("protection_active", "设备处于保护状态，请在上位机检查并排除原因。");
            bool acknowledged = _device.WriteSetpoints(voltage, current);
            bool read = _device.Refresh();
            bool matches = read && Math.Abs(_device.Snapshot.VoltageSet - voltage) < 0.0005 &&
                Math.Abs(_device.Snapshot.CurrentSet - current) < 0.0005;
            return WriteResult(acknowledged, read, matches);
        }, cancellationToken);

    public Task<McpReply> SetOutputAsync(bool enabled, CancellationToken cancellationToken) =>
        ExecuteAsync($"set_output enabled={enabled}", () =>
        {
            var rejection = CheckControl();
            if (rejection is not null) return rejection;
            // Turning off must remain possible even if the pre-read fails or existing settings exceed AI limits.
            if (enabled)
            {
                if (!_device.Refresh()) return Error("read_failed", "开启前读取失败，没有发送开启指令。");
                var data = _device.Snapshot;
                if (data.Protect != 0) return Error("protection_active", "设备处于保护状态，禁止 AI 开启输出。");
                if (!ValidSetpoint(data.VoltageSet, Settings.MaxVoltage) || !ValidSetpoint(data.CurrentSet, Settings.MaxCurrent))
                    return Error("limit_exceeded", "设备现有设定超出 AI 限值，请先在允许范围内设置电压和限流。");
            }
            bool acknowledged = _device.WriteOutput(enabled);
            bool read = _device.Refresh();
            return WriteResult(acknowledged, read, read && (_device.Snapshot.OnOff == (enabled ? 1 : 0)));
        }, cancellationToken);

    private McpReply? CheckControl()
    {
        if (!Settings.AllowControl) return Error("control_disabled", "AI 当前仅可读取；请在上位机的 AI / MCP 设置中启用控制。");
        if (!_device.IsConnected) return Error("not_connected", "请先在上位机串口设置中连接电源。");
        return null;
    }

    private McpReply WriteResult(bool acknowledged, bool read, bool matches)
    {
        if (!acknowledged)
            return new(false, "write_uncertain", "没有收到有效写入确认，设备可能已执行。请先读取状态，不要自动重复写入。",
                true, false, false, read ? State() : null);
        if (!read)
            return new(false, "readback_failed", "设备已确认写入，但回读失败，实际状态未确认。请读取状态，不要自动重复写入。",
                true, true);
        if (!matches)
            return new(false, "readback_mismatch", "设备回读与请求不一致。请检查返回状态，不要自动重复写入。",
                true, true, false, State());
        return new(true, "ok", "设备寄存器已回读确认；不表示输出电压已稳定。", true, true, true, State());
    }

    private JsonNode? State()
    {
        var data = _device.Snapshot;
        return JsonSerializer.SerializeToNode(new
        {
            connected = _device.IsConnected,
            sampledAt = new DateTimeOffset(data.Timestamp),
            data.ProductModelCode, data.SerialNumber, data.FirmwareVersion,
            voltageSetV = data.VoltageSet, currentLimitA = data.CurrentSet,
            voltageOutV = data.VoltageOut, currentOutA = data.CurrentOut, powerOutW = data.PowerOut,
            voltageInV = data.VoltageIn, temperatureC = data.CelsiusTemperature,
            outputEnabled = data.OnOff == 1, protectionCode = data.Protect,
            protection = data.ProtectStatus, modeCode = data.CvCc, mode = data.CvCcMode,
            keyboardLocked = data.Lock == 1
        }, JsonOptions);
    }

    private static bool ValidSetpoint(double value, double max) => double.IsFinite(value) && value >= 0 &&
        value <= max && Math.Abs(value * 1000 - Math.Round(value * 1000)) < 0.000001;

    private static McpReply Error(string code, string message) => new(false, code, message);

    private async Task<McpReply> ExecuteAsync(string operation, Func<McpReply> action, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return Error("busy", "另一个 AI 请求正在执行。稍后重试读取；写入前重新读取状态。");
        try
        {
            var reply = await _dispatch(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_active || !Settings.Enabled) return Error("server_stopping", "MCP 服务正在停止或已关闭。");
                return action();
            }, cancellationToken);
            _audit($"MCP {operation} code={reply.Code} attempted={reply.WriteAttempted} acknowledged={reply.WriteAcknowledged} confirmed={reply.Confirmed}");
            return reply;
        }
        finally { _gate.Release(); }
    }
}
