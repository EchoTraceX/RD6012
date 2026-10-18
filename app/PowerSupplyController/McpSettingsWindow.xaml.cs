using System.Globalization;
using System.Text.Json;
using System.Windows;
using PowerSupplyController.Mcp;

namespace PowerSupplyController;

public partial class McpSettingsWindow : Window
{
    private McpSettings _saved;
    private readonly Func<McpSettings, Task<string>> _apply;

    public McpSettingsWindow(McpSettings settings, string status, Func<McpSettings, Task<string>> apply)
    {
        InitializeComponent();
        _saved = settings;
        _apply = apply;
        StatusText.Text = status;
        EnabledBox.IsChecked = settings.Enabled;
        AllowControlBox.IsChecked = settings.AllowControl;
        PortBox.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        VoltageBox.Text = settings.MaxVoltage.ToString(CultureInfo.InvariantCulture);
        CurrentBox.Text = settings.MaxCurrent.ToString(CultureInfo.InvariantCulture);
        EndpointBox.Text = settings.Endpoint;
        TokenBox.Password = settings.Token;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = _saved with
            {
                Enabled = EnabledBox.IsChecked == true,
                AllowControl = AllowControlBox.IsChecked == true,
                Port = int.Parse(PortBox.Text, CultureInfo.InvariantCulture),
                MaxVoltage = double.Parse(VoltageBox.Text, CultureInfo.InvariantCulture),
                MaxCurrent = double.Parse(CurrentBox.Text, CultureInfo.InvariantCulture),
                Token = TokenBox.Password
            };
            settings.Validate();
            SettingsPanel.IsEnabled = false;
            StatusText.Text = await _apply(settings);
            _saved = settings;
            EndpointBox.Text = settings.Endpoint;
            FeedbackText.Text = "设置已保存。端口或令牌改变后，请更新 AI 客户端配置。";
        }
        catch (Exception ex) { FeedbackText.Text = "应用失败：" + ex.Message; }
        finally { SettingsPanel.IsEnabled = true; }
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e) => Copy(_saved.Token);

    private void CopyConfiguration_Click(object sender, RoutedEventArgs e) => Copy(JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["rd6012"] = new { type = "http", url = _saved.Endpoint,
                headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _saved.Token } }
        }
    }, new JsonSerializerOptions { WriteIndented = true }));

    private void Copy(string value)
    {
        try
        {
            Clipboard.SetText(value);
            FeedbackText.Text = "已复制当前保存的配置。尚未保存的修改不包含在内。";
        }
        catch (Exception ex) { FeedbackText.Text = "无法访问剪贴板：" + ex.Message; }
    }

    private void RotateToken_Click(object sender, RoutedEventArgs e)
    {
        TokenBox.Password = McpSettings.NewToken();
        FeedbackText.Text = "新令牌在保存后生效；旧客户端需要重新配置。";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
