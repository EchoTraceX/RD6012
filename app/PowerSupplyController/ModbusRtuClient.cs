using System.IO.Ports;

namespace PowerSupplyController;

/// <summary>
/// Modbus RTU 通信类
/// </summary>
public class ModbusRtuClient : IDisposable
{
    private const int MinCommandIntervalMs = 320;
    private SerialPort? _serialPort;
    private readonly object _lock = new();
    private long _lastCommandTimestamp = long.MinValue;

    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 115200;
    public int DeviceAddress { get; set; } = 1;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    /// <summary>
    /// 连接串口
    /// </summary>
    public bool Connect()
    {
        try
        {
            DiagnosticLogger.Log(
                $"ModbusRtuClient.Connect begin | port={PortName} baud={BaudRate} addr={DeviceAddress} dataBits={DataBits} parity={Parity} stopBits={StopBits}");
            DiagnosticLogger.Log(
                $"System.IO.Ports assembly | {typeof(SerialPort).Assembly.Location} | {typeof(SerialPort).Assembly.FullName}");
            Disconnect();

            _serialPort = new SerialPort
            {
                PortName = PortName,
                BaudRate = BaudRate,
                DataBits = DataBits,
                Parity = Parity,
                StopBits = StopBits,
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            _serialPort.Open();
            _lastCommandTimestamp = long.MinValue;
            Thread.Sleep(100); // 等待串口稳定
            DiagnosticLogger.Log("ModbusRtuClient.Connect serial open success");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"ModbusRtuClient.Connect failed | {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 断开串口
    /// </summary>
    public void Disconnect()
    {
        _serialPort?.Close();
        _serialPort?.Dispose();
        _serialPort = null;
        _lastCommandTimestamp = long.MinValue;
    }

    /// <summary>
    /// 计算CRC16校验码
    /// </summary>
    private static byte[] CalculateCrc16(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
    }

    private void WaitForCommandWindow()
    {
        if (_lastCommandTimestamp == long.MinValue)
        {
            return;
        }

        long elapsed = Environment.TickCount64 - _lastCommandTimestamp;
        if (elapsed < MinCommandIntervalMs)
        {
            Thread.Sleep((int)(MinCommandIntervalMs - elapsed));
        }
    }

    private void MarkCommandSent()
    {
        _lastCommandTimestamp = Environment.TickCount64;
    }

    private bool TryReadExact(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _serialPort!.Read(buffer, offset + totalRead, count - totalRead);
            if (read <= 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static bool ValidateFrame(byte[] frame)
    {
        if (frame.Length < 4)
        {
            return false;
        }

        byte[] dataPart = new byte[frame.Length - 2];
        Array.Copy(frame, dataPart, dataPart.Length);
        byte[] calculatedCrc = CalculateCrc16(dataPart);
        return frame[^2] == calculatedCrc[0] && frame[^1] == calculatedCrc[1];
    }

    private byte[]? ReadHoldingRegisterResponse(ushort expectedCount)
    {
        byte[] header = new byte[3];
        if (!TryReadExact(header, 0, header.Length))
        {
            return null;
        }

        if (header[0] != DeviceAddress)
        {
            return null;
        }

        if (header[1] == 0x83)
        {
            byte[] exceptionFrame = new byte[5];
            Array.Copy(header, exceptionFrame, header.Length);
            if (!TryReadExact(exceptionFrame, 3, 2))
            {
                return null;
            }

            _ = ValidateFrame(exceptionFrame);
            return null;
        }

        if (header[1] != 0x03)
        {
            return null;
        }

        int byteCount = header[2];
        if (byteCount != expectedCount * 2)
        {
            return null;
        }

        byte[] response = new byte[3 + byteCount + 2];
        Array.Copy(header, response, header.Length);

        if (!TryReadExact(response, 3, byteCount + 2))
        {
            return null;
        }

        return ValidateFrame(response) ? response : null;
    }

    private byte[]? ReadWriteResponse(byte expectedFunction)
    {
        byte[] header = new byte[2];
        if (!TryReadExact(header, 0, header.Length))
        {
            return null;
        }

        if (header[0] != DeviceAddress)
        {
            return null;
        }

        int frameLength = header[1] switch
        {
            var function when function == expectedFunction => 8,
            var function when function == (expectedFunction | 0x80) => 5,
            _ => -1
        };

        if (frameLength < 0)
        {
            return null;
        }

        byte[] response = new byte[frameLength];
        Array.Copy(header, response, header.Length);

        if (!TryReadExact(response, 2, frameLength - 2))
        {
            return null;
        }

        return ValidateFrame(response) ? response : null;
    }

    /// <summary>
    /// 读取寄存器 (功能码 0x03)
    /// 读取指定地址开始的保持寄存器
    /// </summary>
    public ushort[]? ReadHoldingRegisters(ushort startAddress, ushort count)
    {
        lock (_lock)
        {
            if (!IsConnected) return null;

            try
            {
                // 构建命令
                byte[] command = new byte[6];
                command[0] = (byte)DeviceAddress;
                command[1] = 0x03;
                command[2] = (byte)(startAddress >> 8);
                command[3] = (byte)(startAddress & 0xFF);
                command[4] = (byte)(count >> 8);
                command[5] = (byte)(count & 0xFF);

                // 添加CRC
                byte[] crc = CalculateCrc16(command);
                byte[] fullCommand = new byte[8];
                Array.Copy(command, fullCommand, 6);
                Array.Copy(crc, 0, fullCommand, 6, 2);

                // 发送命令
                WaitForCommandWindow();
                _serialPort!.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _serialPort.Write(fullCommand, 0, fullCommand.Length);
                MarkCommandSent();

                byte[]? response = ReadHoldingRegisterResponse(count);
                if (response == null)
                {
                    return null;
                }

                // 解析数据
                int byteCount = response[2];
                ushort[] values = new ushort[byteCount / 2];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
                }

                return values;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 写多个寄存器 (功能码 0x10)
    /// RD6012: 设置电压电流使用地址 0x0008
    /// </summary>
    public bool WriteMultipleRegisters(ushort startAddress, ushort[] values)
    {
        lock (_lock)
        {
            if (!IsConnected) return false;

            try
            {
                ushort count = (ushort)values.Length;
                byte byteCount = (byte)(count * 2);

                // 构建命令
                List<byte> command = new List<byte>
                {
                    (byte)DeviceAddress,
                    0x10,  // 功能码：写多个寄存器
                    (byte)(startAddress >> 8),
                    (byte)(startAddress & 0xFF),
                    (byte)(count >> 8),
                    (byte)(count & 0xFF),
                    byteCount
                };

                // 添加寄存器值
                foreach (ushort value in values)
                {
                    command.Add((byte)(value >> 8));
                    command.Add((byte)(value & 0xFF));
                }

                // 添加CRC
                byte[] crc = CalculateCrc16(command.ToArray());
                command.AddRange(crc);

                byte[] fullCommand = command.ToArray();

                // 发送命令
                WaitForCommandWindow();
                _serialPort!.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _serialPort.Write(fullCommand, 0, fullCommand.Length);
                MarkCommandSent();

                byte[]? response = ReadWriteResponse(0x10);
                if (response == null || response.Length != 8)
                {
                    return false;
                }

                ushort responseAddress = (ushort)((response[2] << 8) | response[3]);
                ushort responseCount = (ushort)((response[4] << 8) | response[5]);
                return responseAddress == startAddress && responseCount == count;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 写单个寄存器 (功能码 0x06)
    /// RD6012: 输出开关控制使用地址 0x0012
    /// </summary>
    public bool WriteSingleRegister(ushort address, ushort value)
    {
        lock (_lock)
        {
            if (!IsConnected) return false;

            try
            {
                // 构建命令
                byte[] command = new byte[6];
                command[0] = (byte)DeviceAddress;
                command[1] = 0x06;
                command[2] = (byte)(address >> 8);
                command[3] = (byte)(address & 0xFF);
                command[4] = (byte)(value >> 8);
                command[5] = (byte)(value & 0xFF);

                // 添加CRC
                byte[] crc = CalculateCrc16(command);
                byte[] fullCommand = new byte[8];
                Array.Copy(command, fullCommand, 6);
                Array.Copy(crc, 0, fullCommand, 6, 2);

                // 发送命令
                WaitForCommandWindow();
                _serialPort!.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _serialPort.Write(fullCommand, 0, fullCommand.Length);
                MarkCommandSent();

                byte[]? response = ReadWriteResponse(0x06);
                if (response == null || response.Length != 8)
                {
                    return false;
                }

                ushort responseAddress = (ushort)((response[2] << 8) | response[3]);
                ushort responseValue = (ushort)((response[4] << 8) | response[5]);
                return responseAddress == address && responseValue == value;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
