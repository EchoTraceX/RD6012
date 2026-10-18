using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.IO;
using System.Text.Json;
using System.ComponentModel;
using PowerSupplyController.Mcp;

namespace PowerSupplyController;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _chartPaused;
    private bool _startupConnectionAttempted;
    private McpSettings _mcpSettings = new();
    private PowerSupplyService? _powerSupplyService;
    private EmbeddedMcpServer? _mcpServer;
    private bool _shutdownCompleted;
    private bool _shutdownStarted;
    private bool _mcpChanging;
    private string _mcpStatus = "MCP 尚未启动";
    private double _lastVoltage;
    private double _lastCurrent;
    private double _lastPower;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        InitializeChart();
        SetupKeyboardShortcuts();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void InitializeChart()
    {
        ConfigureChart(Chart);
        ConfigureChart(HtmlChart);
        ApplyChartTheme();
    }

    private void ConfigureChart(LiveChartsCore.SkiaSharpView.WPF.CartesianChart chart)
    {
        chart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _viewModel.VoltageChartData,
                Name = "电压 (V)",
                Stroke = new SolidColorPaint(new SKColor(61, 195, 216)) { StrokeThickness = 2.5f },
                Fill = null,
                GeometrySize = 0
            },
            new LineSeries<double>
            {
                Values = _viewModel.CurrentChartData,
                Name = "电流 (A)",
                Stroke = new SolidColorPaint(new SKColor(79, 210, 142)) { StrokeThickness = 2.5f },
                Fill = null,
                GeometrySize = 0
            },
            new LineSeries<double>
            {
                Values = _viewModel.PowerChartData,
                Name = "功率 (W)",
                Stroke = new SolidColorPaint(new SKColor(255, 179, 71)) { StrokeThickness = 2.5f },
                Fill = null,
                GeometrySize = 0
            }
        };

        chart.XAxes = new[]
        {
            new Axis
            {
                Labels = _viewModel.TimeLabels,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(new SKColor(138, 152, 168))
            }
        };

        chart.YAxes = new[]
        {
            new Axis
            {
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(new SKColor(138, 152, 168)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(38, 50, 66)) { StrokeThickness = 1 }
            }
        };
    }

    private void ApplyCurrentTheme()
    {
        if (_viewModel.IsDarkTheme)
        {
            ThemeManager.ApplyDarkTheme();
        }
        else
        {
            ThemeManager.ApplyLightTheme();
        }

        ApplyChartTheme();
        ApplyWindowShadowTheme();
    }

    private void ApplyChartTheme()
    {
        var labelColor = _viewModel.IsDarkTheme
            ? new SKColor(138, 152, 168)
            : new SKColor(100, 116, 139);
        var separatorColor = _viewModel.IsDarkTheme
            ? new SKColor(38, 50, 66)
            : new SKColor(203, 213, 225);

        UpdateChartTheme(Chart, labelColor, separatorColor);
        UpdateChartTheme(HtmlChart, labelColor, separatorColor);
    }

    private static void UpdateChartTheme(LiveChartsCore.SkiaSharpView.WPF.CartesianChart chart, SKColor labelColor, SKColor separatorColor)
    {
        if (chart.XAxes is not null)
        {
            foreach (var axis in chart.XAxes)
            {
                axis.LabelsPaint = new SolidColorPaint(labelColor);
            }
        }

        if (chart.YAxes is not null)
        {
            foreach (var axis in chart.YAxes)
            {
                axis.LabelsPaint = new SolidColorPaint(labelColor);
                axis.SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 };
            }
        }
    }

    private void ApplyWindowShadowTheme()
    {
        if (WindowShadowEffect is not DropShadowEffect shadow)
        {
            return;
        }

        if (_viewModel.IsDarkTheme)
        {
            shadow.Color = Colors.Black;
            shadow.Opacity = 0.4;
        }
        else
        {
            shadow.Color = (Color)ColorConverter.ConvertFromString("#AAB7C8");
            shadow.Opacity = 0.16;
        }
    }

    private void SetupKeyboardShortcuts()
    {
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                LockButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SettingsButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.CurrentData))
        {
            AnimateValueChange();
        }
    }

    private void AnimateValueChange()
    {
        var currentData = _viewModel.CurrentData;

        if (Math.Abs(currentData.VoltageOut - _lastVoltage) > 0.01)
        {
            AnimateElement(VoltageDisplay);
            _lastVoltage = currentData.VoltageOut;
        }

        if (Math.Abs(currentData.CurrentOut - _lastCurrent) > 0.001)
        {
            AnimateElement(CurrentDisplay);
            _lastCurrent = currentData.CurrentOut;
        }

        if (Math.Abs(currentData.PowerOut - _lastPower) > 0.01)
        {
            AnimateElement(PowerDisplay);
            _lastPower = currentData.PowerOut;
        }
    }

    private void AnimateElement(UIElement element)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0.6,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DiagnosticLogger.LogProcessStart("Window_Loaded");
        AdjustInitialBounds();
        LoadWindowSettings();
        LoadThemePreference();
        LoadSerialConfigPreference();
        UpdateWindowChromeState();

        if (_startupConnectionAttempted)
        {
            return;
        }

        _startupConnectionAttempted = true;
        await InitializeMcpAsync();
        if (_shutdownStarted || Environment.GetCommandLineArgs().Contains("--offline")) return;
        DiagnosticLogger.Log(
            $"Startup connect begin | port={_viewModel.Config.PortName} baud={_viewModel.Config.BaudRate} addr={_viewModel.Config.DeviceAddress} dataBits={_viewModel.Config.DataBits} parity={_viewModel.Config.Parity} stopBits={_viewModel.Config.StopBits}");
        if (_viewModel.Connect() || _viewModel.TryAutoConnectAvailablePorts())
        {
            DiagnosticLogger.Log("Startup connect succeeded");
            SaveSerialConfigPreference();
            return;
        }

        DiagnosticLogger.Log("Startup connect failed, opening settings dialog");
        OpenSettingsDialog(retryUntilSuccess: true);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        UpdateWindowChromeState();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog(retryUntilSuccess: false);
    }

    private async Task InitializeMcpAsync()
    {
        try
        {
            _mcpSettings = McpSettingsStore.Load();
            await ApplyMcpSettingsAsync(_mcpSettings, save: false);
        }
        catch (Exception ex)
        {
            _mcpSettings = new McpSettings { Enabled = false };
            SetMcpStatus("MCP 未启动：" + ex.Message);
            DiagnosticLogger.Log($"MCP startup failed: {ex.GetType().Name}");
        }
    }

    private async Task<string> ApplyMcpSettingsAsync(McpSettings settings, bool save = true)
    {
        if (_shutdownStarted) throw new InvalidOperationException("软件正在关闭。");
        if (_mcpChanging) throw new InvalidOperationException("MCP 正在启动或应用设置，请稍后重试。");
        settings.Validate();
        _mcpChanging = true;
        McpButton.IsEnabled = false;
        try
        {
            _powerSupplyService?.SetActive(false);
            if (_mcpServer is not null) await _mcpServer.StopAsync();
            if (save) McpSettingsStore.Save(settings);
            _mcpSettings = settings;
            _powerSupplyService ??= new PowerSupplyService(new ViewModelPowerSupplyDevice(_viewModel), settings,
                (action, cancellationToken) => Dispatcher.InvokeAsync(action,
                    System.Windows.Threading.DispatcherPriority.Background, cancellationToken).Task,
                DiagnosticLogger.Log);
            _powerSupplyService.Configure(settings);
            _mcpServer ??= new EmbeddedMcpServer(_powerSupplyService);
            if (!_shutdownStarted)
            {
                try { await _mcpServer.StartAsync(); }
                catch (Exception ex)
                {
                    SetMcpStatus("设置已保存，但 MCP 未运行：" + ex.Message);
                    DiagnosticLogger.Log($"MCP start failed: {ex.GetType().Name}");
                    return _mcpStatus;
                }
            }
            SetMcpStatus(_mcpServer.IsRunning
                ? $"MCP 运行中 · {(settings.AllowControl ? "允许控制" : "只读")} · {settings.Endpoint}"
                : "MCP 已关闭");
        }
        catch (Exception ex)
        {
            SetMcpStatus("MCP 未运行：" + ex.Message);
            DiagnosticLogger.Log($"MCP configuration failed: {ex.GetType().Name}");
            throw;
        }
        finally
        {
            _mcpChanging = false;
            McpButton.IsEnabled = !_shutdownStarted;
        }
        return _mcpStatus;
    }

    private void SetMcpStatus(string status)
    {
        _mcpStatus = status;
        McpButton.Content = _mcpServer?.IsRunning == true
            ? (_mcpSettings.AllowControl ? "AI / MCP · 控制" : "AI / MCP · 只读") : "AI / MCP · 未运行";
        McpButton.ToolTip = status;
    }

    private void McpButton_Click(object sender, RoutedEventArgs e)
    {
        new McpSettingsWindow(_mcpSettings, _mcpStatus, settings => ApplyMcpSettingsAsync(settings))
        { Owner = this }.ShowDialog();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_shutdownCompleted) return;
        e.Cancel = true;
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        _powerSupplyService?.SetActive(false);
        try
        {
            // Keep the dispatcher alive until pending HTTP operations have drained.
            while (_mcpChanging) await Task.Delay(25);
            if (_mcpServer is not null) await _mcpServer.StopAsync();
        }
        catch (Exception ex) { DiagnosticLogger.Log($"MCP shutdown: {ex.GetType().Name}"); }
        finally
        {
            _shutdownCompleted = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void OpenSettingsDialog(bool retryUntilSuccess)
    {
        while (true)
        {
            var settingsWindow = new SettingsWindow(_viewModel.Config)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() != true)
            {
                return;
            }

            SaveSerialConfigPreference();
            _viewModel.Disconnect();
            if (_viewModel.Connect())
            {
                SaveSerialConfigPreference();
                return;
            }

            ThemedDialog.ShowFailure(this, "连接失败", "连接失败，请检查串口、设备地址和电源状态。");

            if (!retryUntilSuccess)
            {
                return;
            }
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag != null)
        {
            _viewModel.VoltageSetValue = Convert.ToDouble(button.Tag);
        }
    }

    private void CurrentPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag != null)
        {
            _viewModel.CurrentSetValue = Convert.ToDouble(button.Tag);
        }
    }

    private void SetVoltageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.SetVoltage(_viewModel.VoltageSetValue))
        {
            ThemedDialog.ShowFailure(this, "设置失败", "电压设置失败，请检查当前连接状态和输入值。");
        }
    }

    private void SetCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.SetCurrent(_viewModel.CurrentSetValue))
        {
            ThemedDialog.ShowFailure(this, "设置失败", "电流设置失败，请检查当前连接状态和输入值。");
        }
    }

    private void EnableOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEnableOutput)
        {
            return;
        }

        if (!ThemedDialog.ShowConfirmation(
                this,
                "确认开启输出",
                $"设备当前设定：{_viewModel.CurrentData.VoltageSet:F3} V / 限流 {_viewModel.CurrentData.CurrentSet:F3} A。\n开启后将按该设定向负载供电。",
                "开启输出"))
        {
            return;
        }

        SetOutputFromUi(true);
    }

    private void DisableOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanDisableOutput)
        {
            SetOutputFromUi(false);
        }
    }

    private void SetOutputFromUi(bool enabled)
    {
        if (enabled ? !_viewModel.CanEnableOutput : !_viewModel.CanDisableOutput)
        {
            return;
        }

        if (_viewModel.SetOutput(enabled))
        {
            return;
        }

        ThemedDialog.ShowFailure(
            this,
            "输出控制失败",
            "输出控制失败\n\n" +
            "注意：某些设备型号不支持远程控制输出开关。\n" +
            "如果此功能无法使用，请手动在设备面板上控制输出开关。");
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsDeviceDataValid)
        {
            return;
        }

        bool enable = _viewModel.CurrentData.Lock == 0;
        if (!_viewModel.SetKeyboardLock(enable))
        {
            ThemedDialog.ShowFailure(this, "键盘锁失败", "键盘锁状态切换失败，请检查当前连接状态。");
        }
    }

    private void CommonPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is null)
        {
            return;
        }

        string presetName = button.Tag.ToString() ?? string.Empty;
        PresetProfile? preset = _viewModel.PresetProfiles.FirstOrDefault(item => item.Name == presetName);
        if (preset is null)
        {
            return;
        }

        _viewModel.SelectedPresetProfile = preset;
        _viewModel.VoltageSetValue = preset.Voltage;
        _viewModel.CurrentSetValue = preset.Current;
    }

    private void ApplySelectedPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPresetProfile is not { } preset)
        {
            return;
        }

        _viewModel.VoltageSetValue = preset.Voltage;
        _viewModel.CurrentSetValue = preset.Current;
    }

    private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsDeviceDataValid)
        {
            return;
        }

        if (_viewModel.OutputEnabled &&
            !ThemedDialog.ShowConfirmation(
                this,
                "确认修改输出设定",
                $"输出当前已开启。应用 {_viewModel.VoltageSetValue:F3} V / {_viewModel.CurrentSetValue:F3} A 后，负载将立即受到新设定影响。",
                "应用设定"))
        {
            return;
        }

        if (!_viewModel.IsDeviceDataValid)
        {
            return;
        }

        if (!_viewModel.ApplySetpoints())
        {
            ThemedDialog.ShowFailure(this, "设置失败", "应用电压和电流设定失败，请检查当前连接状态和输入值。");
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.RefreshNow())
        {
            ThemedDialog.ShowFailure(this, "刷新失败", "刷新设备状态失败，请检查通信连接。");
        }
    }

    private void ChartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _chartPaused = !_chartPaused;
        _viewModel.IsChartPaused = _chartPaused;
        ChartPauseButton.Content = _chartPaused ? "▶ 波形继续" : "⏸ 波形暂停";
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsRecording)
        {
            string? filePath = _viewModel.StartRecording();
            if (filePath != null)
            {
                RecordingIndicator.Visibility = Visibility.Visible;
                var blinkAnimation = (Storyboard)FindResource("RecordingBlinkAnimation");
                blinkAnimation.Begin(RecordingIndicator);

                ThemedDialog.ShowSuccess(this, "开始记录", $"数据记录已开始\n\n文件路径：\n{filePath}");
            }
            else
            {
                ThemedDialog.ShowFailure(this, "记录失败", "无法创建记录文件，请检查磁盘空间和权限。");
            }
        }
        else
        {
            string? filePath = _viewModel.StopRecording();
            RecordingIndicator.Visibility = Visibility.Collapsed;
            var blinkAnimation = (Storyboard)FindResource("RecordingBlinkAnimation");
            blinkAnimation.Stop(RecordingIndicator);

            if (filePath != null)
            {
                ThemedDialog.ShowSuccess(this, "停止记录", $"数据记录已停止并保存\n\n文件路径：\n{filePath}");
            }
        }
    }

    private void ClearChartButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearChartData();
    }

    private void OpenRecordsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RD6012_Records");

        try
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            System.Diagnostics.Process.Start("explorer.exe", folderPath);
        }
        catch (Exception ex)
        {
            ThemedDialog.ShowFailure(this, "打开失败", $"无法打开记录文件夹\n\n{ex.Message}");
        }
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsDarkTheme = !_viewModel.IsDarkTheme;
        ApplyCurrentTheme();
        SaveThemePreference();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void AdjustInitialBounds()
    {
        Rect workArea = SystemParameters.WorkArea;
        if (workArea.Width < Width + 40 || workArea.Height < Height + 40)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        Width = Math.Min(Width, workArea.Width * 0.96);
        Height = Math.Min(Height, workArea.Height * 0.97);
    }

    private void UpdateWindowChromeState()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        WindowFrame.Margin = isMaximized ? new Thickness(8) : new Thickness(0);
        WindowFrame.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(16);
        MaximizeIcon.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveWindowSettings();
        _viewModel.Disconnect();
        base.OnClosed(e);
    }

    private void LoadWindowSettings()
    {
        try
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RD6012_PowerSupplyController", "window_settings.txt");
            if (File.Exists(settingsPath))
            {
                var lines = File.ReadAllLines(settingsPath);
                if (lines.Length >= 4)
                {
                    double left = double.Parse(lines[0]);
                    double top = double.Parse(lines[1]);
                    double width = double.Parse(lines[2]);
                    double height = double.Parse(lines[3]);

                    // 确保窗口在屏幕范围内
                    Rect workArea = SystemParameters.WorkArea;
                    if (left >= 0 && top >= 0 && left + width <= workArea.Width && top + height <= workArea.Height)
                    {
                        Left = left;
                        Top = top;
                        Width = width;
                        Height = height;
                    }
                }
            }
        }
        catch
        {
            // 加载失败时使用默认设置
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RD6012_PowerSupplyController");
            Directory.CreateDirectory(settingsDir);

            string settingsPath = Path.Combine(settingsDir, "window_settings.txt");
            File.WriteAllLines(settingsPath, new[]
            {
                Left.ToString(),
                Top.ToString(),
                Width.ToString(),
                Height.ToString()
            });
        }
        catch
        {
            // 保存失败时静默处理
        }
    }

    private void LoadThemePreference()
    {
        try
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RD6012_PowerSupplyController", "theme_settings.txt");
            if (File.Exists(settingsPath))
            {
                string theme = File.ReadAllText(settingsPath).Trim();
                _viewModel.IsDarkTheme = theme == "Dark";
            }
        }
        catch
        {
            // 加载失败时使用默认深色主题
        }

        ApplyCurrentTheme();
    }

    private void SaveThemePreference()
    {
        try
        {
            string settingsDir = GetSettingsDirectory();
            Directory.CreateDirectory(settingsDir);

            string settingsPath = Path.Combine(settingsDir, "theme_settings.txt");
            File.WriteAllText(settingsPath, _viewModel.IsDarkTheme ? "Dark" : "Light");
        }
        catch
        {
            // 保存失败时静默处理
        }
    }

    private void LoadSerialConfigPreference()
    {
        try
        {
            string settingsPath = Path.Combine(GetSettingsDirectory(), "serial_settings.json");
            if (!File.Exists(settingsPath))
            {
                return;
            }

            SerialConfig? config = JsonSerializer.Deserialize<SerialConfig>(File.ReadAllText(settingsPath));
            if (config is null || string.IsNullOrWhiteSpace(config.PortName))
            {
                DiagnosticLogger.Log("LoadSerialConfigPreference skipped due to empty config");
                return;
            }

            _viewModel.ApplySerialConfig(config);
            DiagnosticLogger.Log(
                $"LoadSerialConfigPreference success | port={config.PortName} baud={config.BaudRate} addr={config.DeviceAddress} dataBits={config.DataBits} parity={config.Parity} stopBits={config.StopBits}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"LoadSerialConfigPreference failed | {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SaveSerialConfigPreference()
    {
        try
        {
            string settingsDir = GetSettingsDirectory();
            Directory.CreateDirectory(settingsDir);

            string settingsPath = Path.Combine(settingsDir, "serial_settings.json");
            string json = JsonSerializer.Serialize(_viewModel.Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);
        }
        catch
        {
            // 保存失败时静默处理
        }
    }

    private static string GetSettingsDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RD6012_PowerSupplyController");
    }
}
