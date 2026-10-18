using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using PowerSupplyController;
using PowerSupplyController.Mcp;

namespace RD6012.PowerDesk;

public partial class MainWindow : Window
{
    private const string VirtualHost = "powerdesk.local";
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int MonitorDefaultToNearest = 2;
    private const double WindowCornerRadius = 19d;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RD6012_PowerSupplyController");

    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _snapshotTimer;
    private readonly List<McpAuditEntry> _mcpAudit = new();
    private readonly object _mcpAuditGate = new();

    private bool _webReady;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private bool _startupConnectAttempted;
    private bool _mcpChanging;
    private string _mcpStatus = "MCP 尚未启动";
    private McpSettings _mcpSettings = new();
    private PowerSupplyService? _mcpService;
    private EmbeddedMcpServer? _mcpServer;
    private string? _lastRecordingPath;

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();

        _snapshotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _snapshotTimer.Tick += (_, _) => SendSnapshot();

        Loaded += MainWindow_Loaded;
        StateChanged += Window_StateChanged;
        SizeChanged += (_, _) => UpdateWindowRegion();
        DpiChanged += (_, _) => UpdateWindowRegion();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessageHook);
        UpdateWindowRegion();
    }

    private void UpdateWindowRegion()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (WindowState != WindowState.Normal || ActualWidth <= 0 || ActualHeight <= 0)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double widthDip = ActualWidth > 0 ? ActualWidth : Width;
        double heightDip = ActualHeight > 0 ? ActualHeight : Height;
        if (!double.IsFinite(widthDip) || !double.IsFinite(heightDip))
        {
            return;
        }

        int width = (int)Math.Round(widthDip * dpi.DpiScaleX);
        int height = (int)Math.Round(heightDip * dpi.DpiScaleY);
        int diameter = (int)Math.Round(WindowCornerRadius * 2 * dpi.DpiScaleX);
        if (width <= diameter || height <= diameter)
        {
            return;
        }

        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A borderless WindowChrome window otherwise maximizes over the taskbar.
        // Constrain the maximized bounds to the current monitor's work area.
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            info.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
            info.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
            info.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
            info.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
            Marshal.StructureToPtr(info, lParam, true);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private void StartWindowDrag()
    {
        try
        {
            ReleaseCapture();
            SendMessage(
                new WindowInteropHelper(this).Handle,
                WmNcLButtonDown,
                (IntPtr)HtCaption,
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Window drag failed | {ex.GetType().Name}: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_webReady)
        {
            return;
        }

        UpdateWindowRegion();

        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RD6012_PowerDesk",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            string[] args = Environment.GetCommandLineArgs();
            bool devTools = args.Contains("--devtools", StringComparer.OrdinalIgnoreCase);

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 6, 8, 11);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = devTools;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            string webRoot = Path.Combine(AppContext.BaseDirectory, "web");
            if (!Directory.Exists(webRoot))
            {
                throw new DirectoryNotFoundException($"Web UI folder was not found: {webRoot}");
            }

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                webRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            _webReady = true;
            WebView.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");

            LoadWindowSettings();
            bool hasSavedSerialConfig = LoadSerialConfigPreference() is not null;

            _snapshotTimer.Start();
            await InitializeMcpAsync();

            if (!_startupConnectAttempted && !args.Contains("--offline", StringComparer.OrdinalIgnoreCase))
            {
                _startupConnectAttempted = true;
                await Task.Delay(350);
                TryConnectSavedPort(hasSavedSerialConfig);
            }

            SendWindowState();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"PowerDesk startup failed | {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(
                this,
                "RD6012 Power Desk 启动失败。\n\n" + ex.Message,
                "RD6012 Power Desk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void FitToWorkArea()
    {
        Rect area = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 48));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 48));
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateWindowRegion();
        SendWindowState();
    }

    private void SendWindowState()
    {
        SendWebMessage(new
        {
            type = "windowState",
            maximized = WindowState == WindowState.Maximized,
            width = ActualWidth,
            height = ActualHeight
        });
    }

    private void SendWebMessage(object payload)
    {
        if (!_webReady || WebView.CoreWebView2 is null || _shutdownStarted)
        {
            return;
        }

        try
        {
            WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Web message failed | {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SendResponse(string? requestId, bool ok, string message, object? data = null)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            return;
        }

        SendWebMessage(new { type = "response", requestId, ok, message, data });
    }

    private void SendInitialState()
    {
        SendWebMessage(new
        {
            type = "init",
            serialPorts = SerialPortDiscovery.GetPortNames(),
            config = BuildSerialConfig(),
            mcp = BuildMcpState(),
            application = "RD6012 Power Desk",
            runtime = Environment.Version.ToString()
        });
        SendSnapshot(force: true);
        SendWindowState();
    }

    private void SendSerialPorts()
    {
        SendWebMessage(new
        {
            type = "serialPorts",
            ports = SerialPortDiscovery.GetPortNames(),
            config = BuildSerialConfig()
        });
    }

    private void SendSnapshot(bool force = false)
    {
        if (!_webReady)
        {
            return;
        }

        SendWebMessage(BuildSnapshot());
    }

    private object BuildSnapshot()
    {
        PowerSupplyData data = _viewModel.CurrentData;
        bool connected = _viewModel.IsConnected;
        bool valid = _viewModel.IsDeviceDataValid;
        SerialConfig config = _viewModel.Config;

        return new
        {
            type = "snapshot",
            connected,
            dataValid = valid,
            connectionStatus = _viewModel.ConnectionStatus,
            outputEnabled = valid ? data.OnOff == 1 : _viewModel.OutputEnabled,
            canEnableOutput = _viewModel.CanEnableOutput,
            canDisableOutput = _viewModel.CanDisableOutput,
            voltageSet = valid ? data.VoltageSet : _viewModel.VoltageSetValue,
            currentSet = valid ? data.CurrentSet : _viewModel.CurrentSetValue,
            voltageOut = valid ? data.VoltageOut : 0,
            currentOut = valid ? data.CurrentOut : 0,
            powerOut = valid ? data.PowerOut : 0,
            voltageIn = valid ? data.VoltageIn : 0,
            temperature = valid ? data.Temperature : 0,
            keyboardLock = valid ? data.Lock : 0,
            protect = valid ? data.Protect : 0,
            protectStatus = valid ? data.ProtectStatus : "--",
            lockStatus = valid ? data.LockStatus : "--",
            outputStatus = valid ? data.OutputStatus : "--",
            mode = valid ? data.CvCcMode : "--",
            modeCode = valid ? data.CvCc : -1,
            sampleStatus = _viewModel.SampleStatusText,
            refreshRate = _viewModel.RefreshRate,
            sampleTicks = valid ? data.Timestamp.Ticks : 0,
            sampledAt = valid ? data.Timestamp.ToString("o") : null,
            config = BuildSerialConfig(),
            device = new
            {
                model = valid ? data.ProductModelDisplay : "--",
                serial = valid ? data.SerialNumberDisplay : "--",
                firmware = valid ? data.FirmwareVersionDisplay : "--",
                temperature = valid ? data.TemperatureDisplay : "--"
            },
            recording = _viewModel.IsRecording,
            recordingPath = _lastRecordingPath,
            stats = new
            {
                maxVoltage = _viewModel.MaxVoltage,
                avgVoltage = _viewModel.AvgVoltage,
                maxCurrent = _viewModel.MaxCurrent,
                avgCurrent = _viewModel.AvgCurrent,
                maxPower = _viewModel.MaxPower
            }
        };
    }

    private object BuildSerialConfig()
    {
        SerialConfig config = _viewModel.Config;
        return new
        {
            portName = config.PortName,
            baudRate = config.BaudRate,
            deviceAddress = config.DeviceAddress,
            dataBits = config.DataBits,
            parity = config.Parity,
            stopBits = config.StopBits
        };
    }

    private object BuildMcpState()
    {
        McpAuditEntry[] audit;
        lock (_mcpAuditGate)
        {
            audit = _mcpAudit.ToArray();
        }

        return new
        {
            type = "mcpState",
            settings = new
            {
                enabled = _mcpSettings.Enabled,
                allowControl = _mcpSettings.AllowControl,
                port = _mcpSettings.Port,
                maxVoltage = _mcpSettings.MaxVoltage,
                maxCurrent = _mcpSettings.MaxCurrent,
                token = _mcpSettings.Token
            },
            endpoint = _mcpSettings.Endpoint,
            running = _mcpServer?.IsRunning == true,
            status = _mcpStatus,
            audit
        };
    }

    private void SendMcpState()
    {
        SendWebMessage(BuildMcpState());
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw = e.WebMessageAsJson;
        string? requestId = null;
        string type = "";

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            type = root.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() ?? "" : "";
            requestId = root.TryGetProperty("requestId", out JsonElement requestElement) ? requestElement.GetString() : null;

            switch (type)
            {
                case "ready":
                    SendInitialState();
                    break;

                case "log":
                    DiagnosticLogger.Log("Web UI | " +
                        (root.TryGetProperty("message", out JsonElement logMessage) ? logMessage.GetString() : ""));
                    break;

                case "window.drag":
                    if (WindowState == WindowState.Normal)
                    {
                        StartWindowDrag();
                    }
                    break;

                case "window.control":
                    HandleWindowControl(root);
                    break;

                case "serial.ports":
                    SendSerialPorts();
                    break;

                case "serial.connect":
                    ConnectSerial(root, requestId);
                    break;

                case "setpoints.apply":
                    ApplySetpoints(root, requestId);
                    break;

                case "output.set":
                    ApplyOutput(root, requestId);
                    break;

                case "lock.set":
                    ApplyKeyboardLock(root, requestId);
                    break;

                case "telemetry.refresh":
                    RefreshTelemetry(requestId);
                    break;

                case "recording.start":
                    StartRecording(requestId);
                    break;

                case "recording.stop":
                    StopRecording(requestId);
                    break;

                case "records.open":
                    OpenRecordsFolder(requestId);
                    break;

                case "mcp.get":
                    SendMcpState();
                    break;

                case "mcp.rotate":
                    RotateMcpToken(requestId);
                    break;

                case "mcp.save":
                    await SaveMcpSettingsAsync(root, requestId);
                    break;

                case "app.minimize":
                    WindowState = WindowState.Minimized;
                    break;

                case "app.close":
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Web command failed | type={type} | {ex.GetType().Name}: {ex.Message}");
            SendResponse(requestId, false, "操作失败：" + ex.Message);
        }
    }

    private void HandleWindowControl(JsonElement root)
    {
        string action = root.TryGetProperty("action", out JsonElement actionElement) ? actionElement.GetString() ?? "" : "";
        switch (action)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "maximize":
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case "close":
                Close();
                break;
        }
    }

    private void ConnectSerial(JsonElement root, string? requestId)
    {
        JsonElement configElement = root.GetProperty("config");
        var config = new SerialConfig
        {
            PortName = configElement.GetProperty("portName").GetString()?.Trim() ?? "",
            BaudRate = configElement.GetProperty("baudRate").GetInt32(),
            DeviceAddress = configElement.GetProperty("deviceAddress").GetInt32(),
            DataBits = configElement.GetProperty("dataBits").GetInt32(),
            Parity = configElement.GetProperty("parity").GetString() ?? "None",
            StopBits = configElement.GetProperty("stopBits").GetInt32()
        };

        if (string.IsNullOrWhiteSpace(config.PortName) || config.BaudRate <= 0 ||
            config.DeviceAddress is < 1 or > 247 || config.DataBits is < 5 or > 8 ||
            config.StopBits is not (1 or 2))
        {
            SendResponse(requestId, false, "串口参数无效，请检查端口、波特率和设备地址。");
            return;
        }

        _viewModel.Disconnect();
        _viewModel.ApplySerialConfig(config);
        bool connected = _viewModel.Connect();
        if (connected)
        {
            SaveSerialConfigPreference();
            SendResponse(requestId, true, $"{config.PortName} 已连接");
            SendSnapshot(force: true);
            SendSerialPorts();
        }
        else
        {
            SendResponse(requestId, false, "连接失败，请检查串口、设备地址和电源状态。");
            SendSnapshot(force: true);
        }
    }

    private void TryConnectSavedPort(bool hadSavedConfig)
    {
        bool connected;
        try
        {
            connected = _viewModel.Connect();
            if (!connected)
            {
                _viewModel.Disconnect();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Startup connect failed | {ex.GetType().Name}: {ex.Message}");
            _viewModel.Disconnect();
            connected = false;
        }

        if (!connected)
        {
            try
            {
                connected = _viewModel.TryAutoConnectAvailablePorts();
                if (connected)
                {
                    SaveSerialConfigPreference();
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log($"Startup port scan failed | {ex.GetType().Name}: {ex.Message}");
            }
        }

        SendSnapshot(force: true);
        SendWebMessage(new
        {
            type = "startupConnection",
            success = connected,
            message = connected
                ? $"{_viewModel.Config.PortName} 已自动连接"
                : hadSavedConfig
                    ? "未检测到可连接的电源，请打开串口设置。"
                    : "未自动发现电源，请打开串口设置选择端口。"
        });
    }

    private void ApplySetpoints(JsonElement root, string? requestId)
    {
        if (!_viewModel.IsDeviceDataValid)
        {
            SendResponse(requestId, false, "设备未连接，无法写入设定。");
            return;
        }

        double voltage = root.GetProperty("voltage").GetDouble();
        double current = root.GetProperty("current").GetDouble();
        _viewModel.VoltageSetValue = voltage;
        _viewModel.CurrentSetValue = current;

        bool success = _viewModel.ApplySetpoints();
        SendResponse(requestId, success,
            success ? $"设定已写入 · {voltage:F3} V / {current:F3} A" : "写入设定失败，请检查串口连接。");
        SendSnapshot(force: true);
    }

    private void ApplyOutput(JsonElement root, string? requestId)
    {
        if (!_viewModel.IsDeviceDataValid)
        {
            SendResponse(requestId, false, "设备未连接，无法控制输出。");
            return;
        }

        bool enabled = root.GetProperty("enabled").GetBoolean();
        if (enabled ? !_viewModel.CanEnableOutput : !_viewModel.CanDisableOutput)
        {
            SendResponse(requestId, false, "当前状态不允许执行该输出操作。");
            return;
        }

        bool success = _viewModel.SetOutput(enabled);
        SendResponse(requestId, success,
            success ? (enabled ? "输出已开启" : "输出已关闭") : "输出控制失败。某些机型不支持远程控制输出。");
        SendSnapshot(force: true);
    }

    private void ApplyKeyboardLock(JsonElement root, string? requestId)
    {
        if (!_viewModel.IsDeviceDataValid)
        {
            SendResponse(requestId, false, "设备未连接，无法控制键盘锁。");
            return;
        }

        bool enabled = root.GetProperty("enabled").GetBoolean();
        bool success = _viewModel.SetKeyboardLock(enabled);
        SendResponse(requestId, success,
            success ? (enabled ? "键盘锁已开启" : "键盘锁已关闭") : "键盘锁状态切换失败。");
        SendSnapshot(force: true);
    }

    private void RefreshTelemetry(string? requestId)
    {
        bool success = _viewModel.RefreshNow();
        SendResponse(requestId, success, success ? "读数已刷新" : "刷新失败，请检查串口连接。");
        SendSnapshot(force: true);
    }

    private void StartRecording(string? requestId)
    {
        if (_viewModel.IsRecording)
        {
            SendResponse(requestId, true, "已经在记录中。", new { path = _lastRecordingPath });
            return;
        }

        string? path = _viewModel.StartRecording();
        _lastRecordingPath = path;
        SendResponse(requestId, path is not null,
            path is not null ? "已开始记录数据" : "无法创建记录文件，请检查磁盘权限。",
            new { path });
        SendWebMessage(new { type = "recordingState", recording = _viewModel.IsRecording, path });
        SendSnapshot(force: true);
    }

    private void StopRecording(string? requestId)
    {
        string? path = _viewModel.StopRecording();
        if (path is not null)
        {
            _lastRecordingPath = path;
        }

        SendResponse(requestId, true, path is not null ? $"记录已保存\n{path}" : "记录已停止。", new { path });
        SendWebMessage(new { type = "recordingState", recording = false, path });
        SendSnapshot(force: true);
    }

    private void OpenRecordsFolder(string? requestId)
    {
        string folderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "RD6012_Records");

        try
        {
            Directory.CreateDirectory(folderPath);
            System.Diagnostics.Process.Start("explorer.exe", folderPath);
            SendResponse(requestId, true, "已打开记录文件夹。");
        }
        catch (Exception ex)
        {
            SendResponse(requestId, false, "无法打开记录文件夹：" + ex.Message);
        }
    }

    private void RotateMcpToken(string? requestId)
    {
        SendResponse(requestId, true, "已生成新令牌，保存后生效。", new { token = McpSettings.NewToken() });
    }

    private async Task SaveMcpSettingsAsync(JsonElement root, string? requestId)
    {
        try
        {
            JsonElement settingsElement = root.GetProperty("settings");
            McpSettings settings = _mcpSettings with
            {
                Enabled = settingsElement.GetProperty("enabled").GetBoolean(),
                AllowControl = settingsElement.GetProperty("allowControl").GetBoolean(),
                Port = settingsElement.GetProperty("port").GetInt32(),
                MaxVoltage = settingsElement.GetProperty("maxVoltage").GetDouble(),
                MaxCurrent = settingsElement.GetProperty("maxCurrent").GetDouble(),
                Token = settingsElement.GetProperty("token").GetString() ?? ""
            };

            settings.Validate();
            await ApplyMcpSettingsAsync(settings, save: true);
            bool running = _mcpServer?.IsRunning == true;
            bool expected = !settings.Enabled || running;
            string message = settings.Enabled
                ? (running ? "MCP 设置已应用。" : "MCP 设置已保存，但服务未能启动：" + _mcpStatus)
                : "MCP 服务已关闭。";
            SendResponse(requestId, expected, message, BuildMcpState());
        }
        catch (Exception ex)
        {
            SendResponse(requestId, false, "MCP 设置保存失败：" + ex.Message);
        }
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
            _mcpStatus = "MCP 未启动：" + ex.Message;
            DiagnosticLogger.Log($"MCP startup failed | {ex.GetType().Name}: {ex.Message}");
            SendMcpState();
        }
    }

    private async Task<string> ApplyMcpSettingsAsync(McpSettings settings, bool save = true)
    {
        if (_shutdownStarted)
        {
            throw new InvalidOperationException("软件正在关闭。");
        }

        if (_mcpChanging)
        {
            throw new InvalidOperationException("MCP 正在启动或应用设置，请稍后重试。");
        }

        settings.Validate();
        _mcpChanging = true;

        try
        {
            _mcpService?.SetActive(false);
            if (_mcpServer is not null)
            {
                await _mcpServer.StopAsync();
            }

            if (save)
            {
                McpSettingsStore.Save(settings);
            }

            _mcpSettings = settings;
            _mcpService ??= new PowerSupplyService(
                new ViewModelPowerSupplyDevice(_viewModel),
                settings,
                (action, cancellationToken) => Dispatcher.InvokeAsync(
                    action,
                    DispatcherPriority.Background,
                    cancellationToken).Task,
                AuditMcp);
            _mcpService.Configure(settings);
            _mcpServer ??= new EmbeddedMcpServer(_mcpService);

            if (!_shutdownStarted)
            {
                try
                {
                    await _mcpServer.StartAsync();
                }
                catch (Exception ex)
                {
                    _mcpStatus = "设置已保存，但 MCP 未运行：" + ex.Message;
                    DiagnosticLogger.Log($"MCP start failed | {ex.GetType().Name}: {ex.Message}");
                    SendMcpState();
                    return _mcpStatus;
                }
            }

            _mcpStatus = _mcpServer.IsRunning
                ? $"MCP 运行中 · {(_mcpSettings.AllowControl ? "允许控制" : "只读")} · {_mcpSettings.Endpoint}"
                : "MCP 已关闭";
        }
        catch (Exception ex)
        {
            _mcpStatus = "MCP 未运行：" + ex.Message;
            DiagnosticLogger.Log($"MCP configuration failed | {ex.GetType().Name}: {ex.Message}");
            SendMcpState();
            throw;
        }
        finally
        {
            _mcpChanging = false;
            SendMcpState();
        }

        return _mcpStatus;
    }

    private void AuditMcp(string line)
    {
        DiagnosticLogger.Log(line);

        var entry = new McpAuditEntry(DateTime.Now.ToString("HH:mm:ss"), line, !line.Contains("code=ok", StringComparison.Ordinal));
        lock (_mcpAuditGate)
        {
            _mcpAudit.Insert(0, entry);
            if (_mcpAudit.Count > 12)
            {
                _mcpAudit.RemoveAt(_mcpAudit.Count - 1);
            }
        }

        if (Dispatcher.CheckAccess())
        {
            SendMcpState();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(SendMcpState));
        }
    }

    private string? LoadSerialConfigPreference()
    {
        try
        {
            string path = Path.Combine(SettingsDirectory, "serial_settings.json");
            if (!File.Exists(path))
            {
                return null;
            }

            SerialConfig? config = JsonSerializer.Deserialize<SerialConfig>(File.ReadAllText(path));
            if (config is null || string.IsNullOrWhiteSpace(config.PortName))
            {
                return null;
            }

            _viewModel.ApplySerialConfig(config);
            return config.PortName;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"LoadSerialConfigPreference failed | {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void SaveSerialConfigPreference()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string path = Path.Combine(SettingsDirectory, "serial_settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(_viewModel.Config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"SaveSerialConfigPreference failed | {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LoadWindowSettings()
    {
        try
        {
            string path = Path.Combine(SettingsDirectory, "web_window_settings.txt");
            if (!File.Exists(path))
            {
                return;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 4)
            {
                return;
            }

            double left = double.Parse(lines[0]);
            double top = double.Parse(lines[1]);
            double width = double.Parse(lines[2]);
            double height = double.Parse(lines[3]);
            Rect area = SystemParameters.WorkArea;

            if (left >= area.Left - 20 && top >= area.Top - 20 &&
                left + width <= area.Right + 20 && top + height <= area.Bottom + 20)
            {
                Left = left;
                Top = top;
                Width = Math.Max(MinWidth, width);
                Height = Math.Max(MinHeight, height);
            }
        }
        catch
        {
            // Ignore invalid persisted window bounds.
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            Rect bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllLines(
                Path.Combine(SettingsDirectory, "web_window_settings.txt"),
                new[]
                {
                    bounds.Left.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bounds.Top.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }
        catch
        {
            // Ignore persistence failures on exit.
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _snapshotTimer.Stop();

        try
        {
            SaveWindowSettings();
            while (_mcpChanging)
            {
                await Task.Delay(25);
            }

            if (_mcpServer is not null)
            {
                await _mcpServer.StopAsync();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"PowerDesk shutdown failed | {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _viewModel.Disconnect();
            _shutdownCompleted = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private sealed record McpAuditEntry(string time, string operation, bool failed);
}
