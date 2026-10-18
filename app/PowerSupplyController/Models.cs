namespace PowerSupplyController;

/// <summary>
/// 电源数据模型
/// </summary>
public class PowerSupplyData
{
    public int ProductModelCode { get; set; }   // 产品型号代码
    public uint SerialNumber { get; set; }      // 产品序列号
    public int FirmwareVersion { get; set; }    // 固件版本原始值
    public int CelsiusSign { get; set; }        // 摄氏温度正负（0正 1负）
    public int CelsiusValue { get; set; }       // 摄氏温度数值
    public double VoltageSet { get; set; }      // 电压设定值 (V)
    public double CurrentSet { get; set; }      // 电流设定值 (A)
    public double VoltageOut { get; set; }      // 输出电压 (V)
    public double CurrentOut { get; set; }      // 输出电流 (A)
    public double PowerOut { get; set; }        // 输出功率 (W)
    public double VoltageIn { get; set; }       // 输入电压 (V)
    public int Lock { get; set; }               // 键盘锁
    public int Protect { get; set; }            // 保护状态
    public int CvCc { get; set; }               // CV/CC状态 (0=CV, 1=CC)
    public int OnOff { get; set; }              // 输出开关 (0=关, 1=开)
    public DateTime Timestamp { get; set; }     // 时间戳

    public bool HasDeviceIdentity => ProductModelCode > 0 || SerialNumber > 0 || FirmwareVersion > 0;
    public string ProductModelDisplay => ProductModelCode > 0 ? $"RD{ProductModelCode}" : "--";
    public string ProductModelCodeDisplay => ProductModelCode > 0 ? ProductModelCode.ToString() : "--";
    public string SerialNumberDisplay => SerialNumber > 0 ? SerialNumber.ToString() : "--";
    public int CelsiusTemperature => CelsiusSign == 1 ? -CelsiusValue : CelsiusValue;
    public double Temperature => CelsiusTemperature;
    public string TemperatureDisplay => HasDeviceIdentity ? $"{CelsiusTemperature} °C" : "--";
    public string FirmwareVersionDisplay => FormatFirmwareVersion(FirmwareVersion);
    public string CvCcMode => CvCc == 0 ? "CV 恒压" : "CC 恒流";
    public string OutputStatus => OnOff == 1 ? "开启" : "关闭";
    public string LockStatus => Lock == 0 ? "未锁定" : "已锁定";
    public string ProtectStatus => Protect switch
    {
        0 => "正常",
        1 => "OVP 过压",
        2 => "OCP 过流",
        3 => "OTP 过温",
        _ => $"未知({Protect})"
    };

    public PowerSupplyData Clone()
    {
        return (PowerSupplyData)MemberwiseClone();
    }

    private static string FormatFirmwareVersion(int firmwareVersion)
    {
        if (firmwareVersion <= 0)
        {
            return "--";
        }

        return firmwareVersion switch
        {
            >= 100 => $"{firmwareVersion / 100}.{firmwareVersion % 100:00}",
            >= 10 => $"{firmwareVersion / 10}.{firmwareVersion % 10}",
            _ => firmwareVersion.ToString()
        };
    }
}

/// <summary>
/// 串口配置
/// </summary>
public class SerialConfig
{
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 115200;
    public int DeviceAddress { get; set; } = 1;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";
    public int StopBits { get; set; } = 1;

    public SerialConfig Clone()
    {
        return new SerialConfig
        {
            PortName = PortName,
            BaudRate = BaudRate,
            DeviceAddress = DeviceAddress,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits
        };
    }
}

public class LayoutOption
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
}

public class PresetProfile
{
    public required string Name { get; init; }
    public required double Voltage { get; init; }
    public required double Current { get; init; }

    public string DisplayName => $"{Name}  {Voltage:F1}V / {Current:F1}A";
}

/// <summary>
/// 数据记录（用于缓冲）
/// </summary>
public class DataRecord
{
}
