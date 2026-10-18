using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PowerSupplyController;

public partial class SettingsWindow : Window
{
    private readonly SerialConfig _config;

    public SettingsWindow(SerialConfig config)
    {
        InitializeComponent();
        _config = config;

        RefreshPortList();
        LoadConfig();
    }

    private void RefreshPortList()
    {
        string? previousSelection = PortComboBox.SelectedItem as string;
        string fallbackPort = string.IsNullOrWhiteSpace(_config.PortName) ? "COM3" : _config.PortName;

        var ports = SerialPortDiscovery.GetPortNames().ToList();

        if (ports.Count == 0)
        {
            ports.Add(fallbackPort);
        }
        else if (!ports.Contains(fallbackPort, StringComparer.OrdinalIgnoreCase))
        {
            ports.Insert(0, fallbackPort);
        }

        PortComboBox.ItemsSource = ports;
        PortComboBox.SelectedItem =
            ports.FirstOrDefault(port => string.Equals(port, previousSelection, StringComparison.OrdinalIgnoreCase)) ??
            ports.FirstOrDefault(port => string.Equals(port, _config.PortName, StringComparison.OrdinalIgnoreCase)) ??
            ports.First();
    }
    private void LoadConfig()
    {
        PortComboBox.SelectedItem =
            PortComboBox.Items.Cast<string>()
                .FirstOrDefault(item => string.Equals(item, _config.PortName, StringComparison.OrdinalIgnoreCase)) ??
            PortComboBox.SelectedItem;

        BaudRateComboBox.SelectedItem = BaudRateComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Content.ToString() == _config.BaudRate.ToString());

        DeviceAddressTextBox.Text = _config.DeviceAddress.ToString();

        DataBitsComboBox.SelectedItem = DataBitsComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Content.ToString() == _config.DataBits.ToString());

        ParityComboBox.SelectedIndex = _config.Parity switch
        {
            "Odd" => 1,
            "Even" => 2,
            _ => 0
        };

        StopBitsComboBox.SelectedIndex = _config.StopBits == 2 ? 1 : 0;
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshPortList();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _config.PortName = PortComboBox.SelectedItem?.ToString() ?? _config.PortName;
            _config.BaudRate = int.Parse(((ComboBoxItem)BaudRateComboBox.SelectedItem).Content.ToString()!);
            _config.DeviceAddress = int.Parse(DeviceAddressTextBox.Text);
            _config.DataBits = int.Parse(((ComboBoxItem)DataBitsComboBox.SelectedItem).Content.ToString()!);

            _config.Parity = ParityComboBox.SelectedIndex switch
            {
                1 => "Odd",
                2 => "Even",
                _ => "None"
            };

            _config.StopBits = StopBitsComboBox.SelectedIndex == 1 ? 2 : 1;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ThemedDialog.ShowFailure(this, "保存设置失败", $"保存设置失败: {ex.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
