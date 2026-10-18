using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PowerSupplyController;
using PowerSupplyController.Mcp;

// No serial client is created. All mutations target this in-memory fake.
int assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    assertions++;
}
var device = new FakeDevice();
var settings = new McpSettings { Port = FreePort() };
var audit = new List<string>();
var service = new PowerSupplyService(device, settings, (action, _) => Task.FromResult(action()), audit.Add);
service.SetActive(true);
var ct = CancellationToken.None;
if (args.Contains("--evaluation-server"))
{
    // A reproducible read-only fixture for the accompanying agent evaluation questions.
    // The public token below is only for this fake, and is never used by the WPF application.
    device.IsConnected = true;
    service.Configure(settings with { Port = 16012, Token = new string('0', 64), AllowControl = false });
    await using var fixture = new EmbeddedMcpServer(service);
    await fixture.StartAsync();
    Console.WriteLine("Read-only FAKE device: http://127.0.0.1:16012/mcp ; Bearer token: 64 zero characters. Ctrl+C to stop.");
    using var fixtureStop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; fixtureStop.Cancel(); };
    try { await Task.Delay(Timeout.Infinite, fixtureStop.Token); }
    catch (OperationCanceledException) { }
    return;
}
Check((await service.GetStatusAsync(ct)).Code == "not_connected", "disconnected status");
Check((await service.GetCapabilitiesAsync(ct)).Success, "offline capabilities");
Check((await service.SetSetpointsAsync(5, 0.5, ct)).Code == "control_disabled", "read-only denies writes");
Check(device.Writes == 0, "no writes in read-only mode");
settings = settings with { AllowControl = true };
service.Configure(settings);
Check((await service.SetOutputAsync(true, ct)).Code == "not_connected", "disconnected output");
device.IsConnected = true;
foreach (double value in new[] { double.NaN, double.PositiveInfinity, -1, 5.001, 0.0001 })
    Check((await service.SetSetpointsAsync(value, 0.5, ct)).Code == "invalid_setpoints", "reject invalid voltage " + value);
foreach (double value in new[] { double.NaN, double.NegativeInfinity, -1, 0.501, 0.0001 })
    Check((await service.SetSetpointsAsync(5, value, ct)).Code == "invalid_setpoints", "reject invalid current " + value);
Check(device.Writes == 0, "invalid requests never write");
device.ReadsSucceed = false;
Check((await service.GetStatusAsync(ct)).Data is null, "no stale data after read failure");
Check((await service.SetSetpointsAsync(5, 0.5, ct)).Code == "read_failed", "pre-read failure");
Check((await service.SetOutputAsync(true, ct)).Code == "read_failed", "enable requires pre-read");
Check(device.Writes == 0, "failed pre-reads never write");
device.ReadsSucceed = true;
device.Snapshot.Protect = 2;
Check((await service.SetSetpointsAsync(5, 0.5, ct)).Code == "protection_active", "protection blocks setting");
Check((await service.SetOutputAsync(true, ct)).Code == "protection_active", "protection blocks enabling");
device.Snapshot.VoltageSet = 12;
Check((await service.SetOutputAsync(false, ct)).Confirmed, "off allowed with protection and high setpoint");
device.Snapshot.Protect = 0;
Check((await service.SetOutputAsync(true, ct)).Code == "limit_exceeded", "enable rejects high existing voltage");
device.Snapshot.VoltageSet = 5;
device.Snapshot.CurrentSet = 2;
Check((await service.SetOutputAsync(true, ct)).Code == "limit_exceeded", "enable rejects high existing current");
Check((await service.SetSetpointsAsync(5, 0.5, ct)).Confirmed, "confirmed paired setpoints");
Check((await service.SetOutputAsync(true, ct)).Confirmed, "confirmed enabled");
Check((await service.SetOutputAsync(false, ct)).Confirmed, "confirmed disabled");
device.FailReadAfterWrite = true;
var unconfirmed = await service.SetSetpointsAsync(3.3, 0.3, ct);
Check(unconfirmed.Code == "readback_failed" && unconfirmed.WriteAcknowledged && !unconfirmed.Confirmed && unconfirmed.Data is null,
    "acknowledgement without readback is not confirmation");
device.ReadsSucceed = true;
device.FailReadAfterWrite = false;
device.IgnoreWrites = true;
Check((await service.SetSetpointsAsync(4, 0.2, ct)).Code == "readback_mismatch", "setpoint mismatch");
Check((await service.SetOutputAsync(true, ct)).Code == "readback_mismatch", "output mismatch");
device.IgnoreWrites = false;
device.Acknowledge = false;
var uncertain = await service.SetSetpointsAsync(4, 0.2, ct);
Check(uncertain.Code == "write_uncertain" && uncertain.WriteAttempted && !uncertain.Confirmed, "lost write response remains uncertain");
device.Acknowledge = true;
device.ReadsSucceed = false;
int beforeOff = device.Writes;
Check((await service.SetOutputAsync(false, ct)).Code == "readback_failed" && device.Writes == beforeOff + 1,
    "off attempted without requiring a successful pre-read");
device.ReadsSucceed = true;
Check(audit.All(entry => !entry.Contains(settings.Token)), "audit excludes token");

// Pending requests are bounded; cancellation/disable before dispatch must prevent writing.
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var queued = new PowerSupplyService(device, settings, async (action, token) =>
{
    entered.TrySetResult();
    await release.Task.WaitAsync(token);
    return action();
}, _ => { });
queued.SetActive(true);
int beforeQueued = device.Writes;
var pending = queued.SetOutputAsync(true, ct);
await entered.Task;
Check((await queued.GetStatusAsync(ct)).Code == "busy", "concurrent call returns busy");
queued.SetActive(false);
release.SetResult();
Check((await pending).Code == "server_stopping" && device.Writes == beforeQueued, "stop revokes queued writes");
queued.SetActive(true);
queued.Configure(settings with { AllowControl = false });
Check((await queued.SetOutputAsync(true, ct)).Code == "control_disabled", "control rechecked inside dispatch");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try { await service.SetOutputAsync(true, canceled.Token); Check(false, "cancellation must throw"); }
    catch (OperationCanceledException) { Check(device.Writes == beforeQueued, "cancelled call never writes"); }
}

await using var server = new EmbeddedMcpServer(service);
await server.StartAsync();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
async Task<HttpStatusCode> Send(string? bearer = null, string? origin = null, string? host = null, string? body = null, string? version = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
    if (bearer is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
    if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
    if (host is not null) request.Headers.Host = host;
    if (version is not null) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
    request.Content = new StringContent(body ?? "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json");
    using var response = await http.SendAsync(request);
    return response.StatusCode;
}
Check(await Send() == HttpStatusCode.Unauthorized, "missing token returns 401");
Check(await Send("wrong") == HttpStatusCode.Unauthorized, "wrong token returns 401");
Check(await Send(settings.Token, "https://attacker.example") == HttpStatusCode.Forbidden, "cross-origin blocked");
Check(await Send(settings.Token, "null") == HttpStatusCode.Forbidden, "null origin blocked");
Check(await Send(settings.Token, host: "attacker.example:" + settings.Port) == HttpStatusCode.Forbidden, "host rebinding blocked");
Check(await Send(settings.Token, $"http://127.0.0.1:{settings.Port}") == HttpStatusCode.OK, "same origin allowed");
Check(await Send(settings.Token, version: "1999-01-01") == HttpStatusCode.BadRequest, "unsupported protocol rejected");
Check(await Send(settings.Token, body: new string('x', 17000)) == HttpStatusCode.RequestEntityTooLarge, "body size bounded");

await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(settings.Endpoint), TransportMode = HttpTransportMode.StreamableHttp,
    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + settings.Token }
}));
var tools = await client.ListToolsAsync();
Check(tools.Count == 4, "official SDK initializes and discovers four tools");
Check(tools.Any(tool => tool.Name == "power_supply_set_setpoints"), "setpoints tool discoverable");
var capabilities = await client.CallToolAsync("power_supply_get_capabilities", cancellationToken: ct);
Check(capabilities.IsError != true && capabilities.StructuredContent.HasValue, "structured capabilities via SDK");
var status = await client.CallToolAsync("power_supply_get_status", cancellationToken: ct);
Check(status.IsError != true && status.Content.OfType<TextContentBlock>().Any(), "text and structured status via SDK");
var changed = await client.CallToolAsync("power_supply_set_setpoints",
    new Dictionary<string, object?> { ["voltage_v"] = 3.3, ["current_a"] = 0.2 }, cancellationToken: ct);
Check(changed.IsError != true && device.Snapshot.VoltageSet == 3.3, "end-to-end paired setpoint tool");
var on = await client.CallToolAsync("power_supply_set_output",
    new Dictionary<string, object?> { ["enabled"] = true }, cancellationToken: ct);
Check(on.IsError != true && device.Snapshot.OnOff == 1, "end-to-end output tool");
int beforeInvalid = device.Writes;
var invalid = await client.CallToolAsync("power_supply_set_setpoints",
    new Dictionary<string, object?> { ["voltage_v"] = 61, ["current_a"] = 0.2 }, cancellationToken: ct);
Check(invalid.IsError == true && device.Writes == beforeInvalid, "tool errors use isError and never write");
var missing = await client.CallToolAsync("power_supply_set_setpoints",
    new Dictionary<string, object?> { ["voltage_v"] = 3.3 }, cancellationToken: ct);
Check(missing.IsError == true && device.Writes == beforeInvalid, "missing argument rejected");
await using (var duplicate = new EmbeddedMcpServer(service))
{
    try { await duplicate.StartAsync(); Check(false, "port conflict must fail"); }
    catch (IOException) { Check(true, "port conflict handled"); }
}
await server.StopAsync();
Check(!server.IsRunning, "server stopped");
string oldToken = settings.Token;
settings = settings with { Token = McpSettings.NewToken() };
service.Configure(settings);
await server.StartAsync();
Check(await Send(settings.Token) == HttpStatusCode.OK, "restart and new token work");
Check(await Send(oldToken) == HttpStatusCode.Unauthorized, "old token revoked after rotation");
Console.WriteLine($"PASS: {assertions} assertions; official MCP client + HTTP transport + fake device. No serial hardware accessed.");

static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

sealed class FakeDevice : IPowerSupplyDevice
{
    public bool IsConnected { get; set; }
    public bool ReadsSucceed { get; set; } = true;
    public bool Acknowledge { get; set; } = true;
    public bool IgnoreWrites { get; set; }
    public bool FailReadAfterWrite { get; set; }
    public int Writes { get; private set; }
    public PowerSupplyData Snapshot { get; } = new()
    {
        ProductModelCode = 6012, SerialNumber = 12345, FirmwareVersion = 140,
        VoltageSet = 5, CurrentSet = 0.5, VoltageOut = 0, CurrentOut = 0,
        VoltageIn = 24, CelsiusValue = 25, Timestamp = new DateTime(2026, 1, 1, 12, 0, 0)
    };
    public bool Refresh() => ReadsSucceed;
    public bool WriteSetpoints(double voltage, double current)
    {
        Writes++;
        if (!IgnoreWrites) { Snapshot.VoltageSet = voltage; Snapshot.CurrentSet = current; }
        if (FailReadAfterWrite) ReadsSucceed = false;
        return Acknowledge;
    }
    public bool WriteOutput(bool enabled)
    {
        Writes++;
        if (!IgnoreWrites) Snapshot.OnOff = enabled ? 1 : 0;
        if (FailReadAfterWrite) ReadsSucceed = false;
        return Acknowledge;
    }
}
