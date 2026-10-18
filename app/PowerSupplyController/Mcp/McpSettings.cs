using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerSupplyController.Mcp;

public sealed record McpSettings
{
    public bool Enabled { get; init; } = true;
    public bool AllowControl { get; init; }
    public int Port { get; init; } = 6012;
    public double MaxVoltage { get; init; } = 5;
    public double MaxCurrent { get; init; } = 0.5;
    [JsonIgnore] public string Token { get; init; } = NewToken();
    public string ProtectedToken { get; init; } = "";
    [JsonIgnore] public string Endpoint => $"http://127.0.0.1:{Port}/mcp";

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public void Validate()
    {
        if (Port is < 1024 or > 65535)
            throw new ArgumentException("端口必须在 1024～65535 之间。");
        if (!double.IsFinite(MaxVoltage) || MaxVoltage <= 0 || MaxVoltage > 60 ||
            !double.IsFinite(MaxCurrent) || MaxCurrent <= 0 || MaxCurrent > 12)
            throw new ArgumentException("AI 电压上限须大于 0 且不超过 60V，电流上限须大于 0 且不超过 12A。");
        if (Token.Length != 64 || !Token.All(Uri.IsHexDigit))
            throw new ArgumentException("访问令牌无效，请重新生成令牌。");
    }
}

public static class McpSettingsStore
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RD6012_PowerSupplyController", "mcp_settings.json");

    public static McpSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            var initial = new McpSettings();
            Save(initial);
            return initial;
        }
        var stored = JsonSerializer.Deserialize<McpSettings>(File.ReadAllText(SettingsPath))
            ?? throw new InvalidDataException("MCP 设置文件为空。");
        var settings = stored with
        {
            Token = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored.ProtectedToken), null, DataProtectionScope.CurrentUser))
        };
        settings.Validate();
        return settings;
    }

    public static void Save(McpSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var stored = settings with
        {
            ProtectedToken = Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(settings.Token), null, DataProtectionScope.CurrentUser))
        };
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, overwrite: true);
    }
}
