using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PowerSupplyController;

public class MainViewModel : INotifyPropertyChanged
{
    private const ushort StatusStartAddress = 0x0000;
    private const ushort StatusRegisterCount = 0x0013;
    private const ushort SetpointStartAddress = 0x0008;
    private const ushort LockControlAddress = 0x000F;
    private const ushort OutputControlAddress = 0x0012;
    private const int VoltageScale = 1000;
    private const int CurrentScale = 1000;
    private const int VoltageInScale = 100;
    private const int PowerScale = 1000;

    private readonly ModbusRtuClient _modbusClient;
    private readonly DispatcherTimer _updateTimer;
    private bool _isConnected;
    private string _connectionStatus = "未连接";
    private PowerSupplyData _currentData = new();
    private double _voltageSetValue = 12.0;
    private double _currentSetValue = 1.0;
    private bool _outputEnabled;
    private bool _hasFreshDeviceState;
    private bool _isChartPaused;
    private bool _isRecording;
    private StreamWriter? _csvWriter;
    private DateTime _recordingStartTime;
    private List<DataRecord> _recordBuffer = new();
    private string? _currentRecordingFilePath;
    private DateTime _lastUpdateTime = DateTime.Now;
    private int _updateCount;
    private double _refreshRate;
    private double _maxVoltage;
    private double _minVoltage = double.MaxValue;
    private double _avgVoltage;
    private double _maxCurrent;
    private double _minCurrent = double.MaxValue;
    private double _avgCurrent;
    private double _maxPower;
    private int _statisticsCount;
    private bool _isDarkTheme = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    // 图表数据
    public ObservableCollection<double> VoltageChartData { get; } = new();
    public ObservableCollection<double> CurrentChartData { get; } = new();
    public ObservableCollection<double> PowerChartData { get; } = new();
    public ObservableCollection<string> TimeLabels { get; } = new();
    public ObservableCollection<PresetProfile> PresetProfiles { get; } = new()
    {
        new PresetProfile { Name = "低压测试", Voltage = 1.8, Current = 0.5 },
        new PresetProfile { Name = "单片机", Voltage = 3.3, Current = 1.0 },
        new PresetProfile { Name = "USB 设备", Voltage = 5.0, Current = 2.0 },
        new PresetProfile { Name = "快充协议", Voltage = 9.0, Current = 2.0 },
        new PresetProfile { Name = "12V 轻载", Voltage = 12.0, Current = 1.0 },
        new PresetProfile { Name = "常用供电", Voltage = 12.0, Current = 3.0 },
        new PresetProfile { Name = "风扇/电机", Voltage = 15.0, Current = 2.0 },
        new PresetProfile { Name = "19V 设备", Voltage = 19.0, Current = 3.0 },
        new PresetProfile { Name = "工控 24V", Voltage = 24.0, Current = 2.0 },
        new PresetProfile { Name = "高压待机", Voltage = 48.0, Current = 0.5 }
    };
    public ObservableCollection<LayoutOption> LayoutOptions { get; } = new()
    {
        new LayoutOption { Id = "classic", DisplayName = "控制台布局", Description = "当前分页控制区布局" },
        new LayoutOption { Id = "html", DisplayName = "仪表盘布局", Description = "接近 deepseek_html 的双卡片仪表盘" }
    };

    // 串口配置
    public SerialConfig Config { get; set; } = new();
    private LayoutOption? _selectedLayoutOption;
    private PresetProfile? _selectedPresetProfile;

    public MainViewModel()
    {
        _modbusClient = new ModbusRtuClient();
        _selectedLayoutOption = LayoutOptions.First();
        _selectedPresetProfile = PresetProfiles.FirstOrDefault();

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350) // 厂家协议要求指令间隔大于300ms
        };
        _updateTimer.Tick += UpdateTimer_Tick;
    }

    #region Properties

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            if (!value)
            {
                _hasFreshDeviceState = false;
            }
            OnPropertyChanged();
            ConnectionStatus = value ? $"{Config.PortName} 已连接 | {Config.BaudRate} bps | 地址:{Config.DeviceAddress}" : "未连接";
            NotifyDevicePresentationChanged();
        }
    }

    public bool IsDeviceDataValid => IsConnected && _hasFreshDeviceState;
    public PowerSupplyData? DisplayData => IsDeviceDataValid ? CurrentData : null;
    public string SampleStatusText => IsDeviceDataValid
        ? $"实时数据 · {CurrentData.Timestamp:HH:mm:ss}"
        : CurrentData.Timestamp > DateTime.MinValue
            ? $"数据失效 · 上次读取 {CurrentData.Timestamp:HH:mm:ss}"
            : "无实时数据";
    public string AppliedVoltageText => IsDeviceDataValid ? $"设备设定 {CurrentData.VoltageSet:F3} V" : "设备设定 -- V";
    public string AppliedCurrentText => IsDeviceDataValid ? $"设备限流 {CurrentData.CurrentSet:F3} A" : "设备限流 -- A";
    public string PendingSetpointsText => $"待应用：{VoltageSetValue:F3} V / {CurrentSetValue:F3} A";
    public bool CanEnableOutput => IsDeviceDataValid && !OutputEnabled;
    public bool CanDisableOutput => IsDeviceDataValid && OutputEnabled;

    private void NotifyDevicePresentationChanged()
    {
        OnPropertyChanged(nameof(IsDeviceDataValid));
        OnPropertyChanged(nameof(DisplayData));
        OnPropertyChanged(nameof(SampleStatusText));
        OnPropertyChanged(nameof(AppliedVoltageText));
        OnPropertyChanged(nameof(AppliedCurrentText));
        OnPropertyChanged(nameof(CanEnableOutput));
        OnPropertyChanged(nameof(CanDisableOutput));
        OnPropertyChanged(nameof(LockButtonText));
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public double RefreshRate
    {
        get => _refreshRate;
        set
        {
            _refreshRate = value;
            OnPropertyChanged();
        }
    }

    public double MaxVoltage
    {
        get => _maxVoltage;
        set
        {
            _maxVoltage = value;
            OnPropertyChanged();
        }
    }

    public double MinVoltage
    {
        get => _minVoltage;
        set
        {
            _minVoltage = value;
            OnPropertyChanged();
        }
    }

    public double AvgVoltage
    {
        get => _avgVoltage;
        set
        {
            _avgVoltage = value;
            OnPropertyChanged();
        }
    }

    public double MaxCurrent
    {
        get => _maxCurrent;
        set
        {
            _maxCurrent = value;
            OnPropertyChanged();
        }
    }

    public double MinCurrent
    {
        get => _minCurrent;
        set
        {
            _minCurrent = value;
            OnPropertyChanged();
        }
    }

    public double AvgCurrent
    {
        get => _avgCurrent;
        set
        {
            _avgCurrent = value;
            OnPropertyChanged();
        }
    }

    public double MaxPower
    {
        get => _maxPower;
        set
        {
            _maxPower = value;
            OnPropertyChanged();
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            _isDarkTheme = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeButtonText));
        }
    }

    public string ThemeButtonText => IsDarkTheme ? "☀ 浅色" : "🌙 深色";

    public PowerSupplyData CurrentData
    {
        get => _currentData;
        set
        {
            _currentData = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockButtonText));
            NotifyDevicePresentationChanged();
        }
    }

    public double VoltageSetValue
    {
        get => _voltageSetValue;
        set
        {
            _voltageSetValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PendingSetpointsText));
        }
    }

    public double CurrentSetValue
    {
        get => _currentSetValue;
        set
        {
            _currentSetValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PendingSetpointsText));
        }
    }

    public bool OutputEnabled
    {
        get => _outputEnabled;
        set
        {
            _outputEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputButtonText));
            OnPropertyChanged(nameof(CanEnableOutput));
            OnPropertyChanged(nameof(CanDisableOutput));
        }
    }

    public string OutputButtonText => OutputEnabled ? "⏻ 关闭输出" : "⏻ 开启输出";

    public string LockButtonText => !IsDeviceDataValid ? "键盘锁 --" : CurrentData.Lock == 1 ? "🔓 解锁键盘" : "🔒 锁定键盘";

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            _isRecording = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordButtonText));
        }
    }

    public string RecordButtonText => IsRecording ? "⏹ 停止记录" : "⏺ 开始记录";

    public bool IsChartPaused
    {
        get => _isChartPaused;
        set
        {
            _isChartPaused = value;
            OnPropertyChanged();
        }
    }

    public LayoutOption? SelectedLayoutOption
    {
        get => _selectedLayoutOption;
        set
        {
            if (_selectedLayoutOption == value || value is null)
            {
                return;
            }

            _selectedLayoutOption = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsClassicLayout));
            OnPropertyChanged(nameof(IsHtmlLayout));
            OnPropertyChanged(nameof(LayoutDescription));
        }
    }

    public bool IsClassicLayout => SelectedLayoutOption?.Id != "html";

    public bool IsHtmlLayout => SelectedLayoutOption?.Id == "html";

    public string LayoutDescription => SelectedLayoutOption?.Description ?? string.Empty;

    public PresetProfile? SelectedPresetProfile
    {
        get => _selectedPresetProfile;
        set
        {
            _selectedPresetProfile = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region Methods

    public void ApplySerialConfig(SerialConfig config)
    {
        Config.PortName = config.PortName;
        Config.BaudRate = config.BaudRate;
        Config.DeviceAddress = config.DeviceAddress;
        Config.DataBits = config.DataBits;
        Config.Parity = config.Parity;
        Config.StopBits = config.StopBits;
        OnPropertyChanged(nameof(Config));

        if (IsConnected)
        {
            ConnectionStatus = $"{Config.PortName} 已连接 | {Config.BaudRate} bps | 地址:{Config.DeviceAddress}";
        }
    }

    public bool Connect()
    {
        try
        {
            DiagnosticLogger.Log(
                $"Connect() begin | port={Config.PortName} baud={Config.BaudRate} addr={Config.DeviceAddress} dataBits={Config.DataBits} parity={Config.Parity} stopBits={Config.StopBits}");
            ApplySerialConfig(Config);
            _modbusClient.PortName = Config.PortName;
            _modbusClient.BaudRate = Config.BaudRate;
            _modbusClient.DeviceAddress = Config.DeviceAddress;
            _modbusClient.DataBits = Config.DataBits;
            _modbusClient.Parity = Config.Parity switch
            {
                "Odd" => Parity.Odd,
                "Even" => Parity.Even,
                _ => Parity.None
            };
            _modbusClient.StopBits = Config.StopBits == 2 ? StopBits.Two : StopBits.One;

            if (!_modbusClient.Connect())
            {
                DiagnosticLogger.Log("Connect() serial open failed");
                return false;
            }

            bool hasValidIdentity = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(220);
                }

                if (!ReadData($"connect attempt {attempt + 1}"))
                {
                    continue;
                }

                if (CurrentData.HasDeviceIdentity)
                {
                    DiagnosticLogger.Log(
                        $"Connect() handshake success on attempt {attempt + 1} | model={CurrentData.ProductModelCode} serial={CurrentData.SerialNumber} fw={CurrentData.FirmwareVersion}");
                    hasValidIdentity = true;
                    break;
                }
            }

            if (!hasValidIdentity)
            {
                DiagnosticLogger.Log("Connect() handshake failed after all attempts");
                Disconnect();
                return false;
            }

            IsConnected = true;
            _updateTimer.Start();
            DiagnosticLogger.Log("Connect() completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Connect() threw {ex.GetType().Name}: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    public bool TryAutoConnectAvailablePorts()
    {
        SerialConfig originalConfig = Config.Clone();
        var ports = SerialPortDiscovery.GetPortNames().ToList();
        DiagnosticLogger.Log($"TryAutoConnectAvailablePorts() ports=[{string.Join(", ", ports)}]");

        foreach (string port in ports)
        {
            if (string.Equals(port, originalConfig.PortName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidateConfig = originalConfig.Clone();
            candidateConfig.PortName = port;
            ApplySerialConfig(candidateConfig);
            DiagnosticLogger.Log($"TryAutoConnectAvailablePorts() trying {port}");

            if (Connect())
            {
                DiagnosticLogger.Log($"TryAutoConnectAvailablePorts() success on {port}");
                return true;
            }
        }

        ApplySerialConfig(originalConfig);
        DiagnosticLogger.Log("TryAutoConnectAvailablePorts() no matching device found");
        return false;
    }

    public void Disconnect()
    {
        _updateTimer.Stop();
        _modbusClient.Disconnect();
        IsConnected = false;
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        bool success = ReadData();

        // 如果读取失败且已连接，尝试重连
        if (!success && IsConnected)
        {
            ConnectionStatus = "连接断开，尝试重连...";
            Disconnect();

            if (Connect())
            {
                return;
            }

            ConnectionStatus = "未连接";
        }

        // 计算刷新率
        _updateCount++;
        var now = DateTime.Now;
        var elapsed = (now - _lastUpdateTime).TotalSeconds;
        if (elapsed >= 1.0)
        {
            RefreshRate = _updateCount / elapsed;
            _updateCount = 0;
            _lastUpdateTime = now;
        }
    }

    private bool ReadData(string? context = null)
    {
        try
        {
            // 厂家文档给出的完整状态区从 0x0000 开始。
            var values = _modbusClient.ReadHoldingRegisters(StatusStartAddress, StatusRegisterCount);
            if (values == null || values.Length < StatusRegisterCount)
            {
                _hasFreshDeviceState = false;
                NotifyDevicePresentationChanged();
                if (context is not null)
                {
                    int actualLength = values?.Length ?? 0;
                    DiagnosticLogger.Log($"ReadData({context}) failed | valuesLength={actualLength}");
                }
                return false;
            }

            uint serialNumber = ((uint)values[1] << 16) | values[2];
            uint powerRaw = ((uint)values[12] << 16) | values[13];

            var data = new PowerSupplyData
            {
                ProductModelCode = values[0],
                SerialNumber = serialNumber,
                FirmwareVersion = values[3],
                CelsiusSign = values[4],
                CelsiusValue = values[5],
                VoltageSet = values[8] / (double)VoltageScale,
                CurrentSet = values[9] / (double)CurrentScale,
                VoltageOut = values[10] / (double)VoltageScale,
                CurrentOut = values[11] / (double)CurrentScale,
                PowerOut = powerRaw / (double)PowerScale,
                VoltageIn = values[14] / (double)VoltageInScale,
                Lock = values[15],
                Protect = values[16],
                CvCc = values[17],
                OnOff = values[18],
                Timestamp = DateTime.Now
            };

            CurrentData = data;
            OutputEnabled = data.OnOff == 1;
            if (!_hasFreshDeviceState)
            {
                VoltageSetValue = data.VoltageSet;
                CurrentSetValue = data.CurrentSet;
            }

            _hasFreshDeviceState = true;
            NotifyDevicePresentationChanged();
            if (context is not null)
            {
                DiagnosticLogger.Log(
                    $"ReadData({context}) success | model={data.ProductModelCode} serial={data.SerialNumber} fw={data.FirmwareVersion} vin={data.VoltageIn:F2} vout={data.VoltageOut:F2} iout={data.CurrentOut:F3}");
            }

            // 更新统计数据
            _statisticsCount++;
            if (data.VoltageOut > MaxVoltage) MaxVoltage = data.VoltageOut;
            if (data.VoltageOut < MinVoltage) MinVoltage = data.VoltageOut;
            AvgVoltage = (AvgVoltage * (_statisticsCount - 1) + data.VoltageOut) / _statisticsCount;

            if (data.CurrentOut > MaxCurrent) MaxCurrent = data.CurrentOut;
            if (data.CurrentOut < MinCurrent) MinCurrent = data.CurrentOut;
            AvgCurrent = (AvgCurrent * (_statisticsCount - 1) + data.CurrentOut) / _statisticsCount;

            if (data.PowerOut > MaxPower) MaxPower = data.PowerOut;

            // 更新图表数据
            if (!IsChartPaused)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    VoltageChartData.Add(data.VoltageOut);
                    CurrentChartData.Add(data.CurrentOut);
                    PowerChartData.Add(data.PowerOut);
                    TimeLabels.Add(data.Timestamp.ToString("HH:mm:ss"));

                    // 保持最多1000个数据点，超过时每2个点保留1个
                    if (VoltageChartData.Count > 1000)
                    {
                        var newVoltageData = new ObservableCollection<double>();
                        var newCurrentData = new ObservableCollection<double>();
                        var newPowerData = new ObservableCollection<double>();
                        var newTimeLabels = new ObservableCollection<string>();

                        for (int i = 0; i < VoltageChartData.Count; i += 2)
                        {
                            newVoltageData.Add(VoltageChartData[i]);
                            newCurrentData.Add(CurrentChartData[i]);
                            newPowerData.Add(PowerChartData[i]);
                            newTimeLabels.Add(TimeLabels[i]);
                        }

                        VoltageChartData.Clear();
                        CurrentChartData.Clear();
                        PowerChartData.Clear();
                        TimeLabels.Clear();

                        foreach (var v in newVoltageData) VoltageChartData.Add(v);
                        foreach (var c in newCurrentData) CurrentChartData.Add(c);
                        foreach (var p in newPowerData) PowerChartData.Add(p);
                        foreach (var t in newTimeLabels) TimeLabels.Add(t);
                    }
                });
            }

            // 记录数据到 CSV
            RecordDataPoint();

            return true;
        }
        catch (Exception ex)
        {
            _hasFreshDeviceState = false;
            NotifyDevicePresentationChanged();
            if (context is not null)
            {
                DiagnosticLogger.Log($"ReadData({context}) threw {ex.GetType().Name}: {ex.Message}");
            }
            return false;
        }
    }

    public bool SetVoltage(double voltage)
    {
        double current = _hasFreshDeviceState ? CurrentData.CurrentSet : CurrentSetValue;
        if (!TryBuildSetpointPayload(voltage, current, out ushort[] payload))
        {
            return false;
        }

        if (!_modbusClient.WriteMultipleRegisters(SetpointStartAddress, payload))
        {
            return false;
        }

        VoltageSetValue = voltage;
        ReadData();
        return true;
    }

    public bool SetCurrent(double current)
    {
        double voltage = _hasFreshDeviceState ? CurrentData.VoltageSet : VoltageSetValue;
        if (!TryBuildSetpointPayload(voltage, current, out ushort[] payload))
        {
            return false;
        }

        if (!_modbusClient.WriteMultipleRegisters(SetpointStartAddress, payload))
        {
            return false;
        }

        CurrentSetValue = current;
        ReadData();
        return true;
    }

    public bool SetOutput(bool enable)
    {
        ushort value = (ushort)(enable ? 1 : 0);
        if (!_modbusClient.WriteSingleRegister(OutputControlAddress, value))
        {
            return false;
        }

        if (!ReadData())
        {
            OutputEnabled = enable;
            var data = CurrentData.Clone();
            data.OnOff = enable ? 1 : 0;
            CurrentData = data;
        }
        return true;
    }

    public bool SetKeyboardLock(bool enable)
    {
        ushort value = (ushort)(enable ? 1 : 0);
        if (!_modbusClient.WriteSingleRegister(LockControlAddress, value))
        {
            return false;
        }

        if (!ReadData())
        {
            var data = CurrentData.Clone();
            data.Lock = enable ? 1 : 0;
            CurrentData = data;
        }
        return true;
    }

    // MCP separates write acknowledgement from readback; never synthesize confirmed device state.
    internal bool WriteMcpSetpoints(double voltage, double current)
    {
        if (!TryBuildSetpointPayload(voltage, current, out ushort[] payload)) return false;
        bool success = _modbusClient.WriteMultipleRegisters(SetpointStartAddress, payload);
        if (success)
        {
            VoltageSetValue = voltage;
            CurrentSetValue = current;
        }
        return success;
    }

    internal bool WriteMcpOutput(bool enabled) =>
        _modbusClient.WriteSingleRegister(OutputControlAddress, (ushort)(enabled ? 1 : 0));

    public bool ApplySetpoints()
    {
        if (!TryBuildSetpointPayload(VoltageSetValue, CurrentSetValue, out ushort[] payload))
        {
            return false;
        }

        if (!_modbusClient.WriteMultipleRegisters(SetpointStartAddress, payload))
        {
            return false;
        }

        ReadData();
        return true;
    }

    public bool ApplyPreset(PresetProfile? preset)
    {
        if (preset is null)
        {
            return false;
        }

        VoltageSetValue = preset.Voltage;
        CurrentSetValue = preset.Current;
        return ApplySetpoints();
    }

    public bool RefreshNow()
    {
        return ReadData();
    }

    public void ClearChartData()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            VoltageChartData.Clear();
            CurrentChartData.Clear();
            PowerChartData.Clear();
            TimeLabels.Clear();
        });

        // 重置统计数据
        _statisticsCount = 0;
        MaxVoltage = 0;
        MinVoltage = double.MaxValue;
        AvgVoltage = 0;
        MaxCurrent = 0;
        MinCurrent = double.MaxValue;
        AvgCurrent = 0;
        MaxPower = 0;
    }

    public string? StartRecording()
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"RD6012_Record_{timestamp}.csv";
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RD6012_Records", fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            _csvWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            _csvWriter.WriteLine("时间戳,相对时间(秒),输出电压(V),输出电流(A),输出功率(W),输入电压(V),温度(℃),模式,输出状态,保护状态");

            _recordingStartTime = DateTime.Now;
            _recordBuffer.Clear();
            _currentRecordingFilePath = filePath;
            IsRecording = true;

            return filePath;
        }
        catch
        {
            _csvWriter?.Dispose();
            _csvWriter = null;
            _currentRecordingFilePath = null;
            return null;
        }
    }

    public string? StopRecording()
    {
        try
        {
            IsRecording = false;
            string? filePath = _currentRecordingFilePath;

            if (_csvWriter != null)
            {
                _csvWriter.Flush();
                _csvWriter.Dispose();
                _csvWriter = null;
            }

            _currentRecordingFilePath = null;
            return filePath;
        }
        catch
        {
            return null;
        }
    }

    private void RecordDataPoint()
    {
        if (!IsRecording || _csvWriter == null)
        {
            return;
        }

        try
        {
            var now = DateTime.Now;
            var elapsed = (now - _recordingStartTime).TotalSeconds;

            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff},{elapsed:F3},{CurrentData.VoltageOut:F3},{CurrentData.CurrentOut:F4},{CurrentData.PowerOut:F3},{CurrentData.VoltageIn:F2},{CurrentData.Temperature:F1},{CurrentData.CvCcMode},{CurrentData.OutputStatus},{CurrentData.ProtectStatus}";

            _csvWriter.WriteLine(line);

            // 每 10 条数据刷新一次，避免频繁写入
            _recordBuffer.Add(new DataRecord());
            if (_recordBuffer.Count >= 10)
            {
                _csvWriter.Flush();
                _recordBuffer.Clear();
            }
        }
        catch
        {
            // 记录失败时静默处理，不影响主程序
        }
    }

    #endregion

    private static bool TryConvertToRegisterValue(double value, int scale, out ushort registerValue)
    {
        registerValue = 0;

        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return false;
        }

        double scaledValue = Math.Round(value * scale, MidpointRounding.AwayFromZero);
        if (scaledValue > ushort.MaxValue)
        {
            return false;
        }

        registerValue = (ushort)scaledValue;
        return true;
    }

    private static bool TryBuildSetpointPayload(double voltage, double current, out ushort[] payload)
    {
        payload = Array.Empty<ushort>();

        if (!TryConvertToRegisterValue(voltage, VoltageScale, out ushort voltageValue) ||
            !TryConvertToRegisterValue(current, CurrentScale, out ushort currentValue))
        {
            return false;
        }

        payload = new[] { voltageValue, currentValue };
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
