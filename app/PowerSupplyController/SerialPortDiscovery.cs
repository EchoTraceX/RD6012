using Microsoft.Win32;

namespace PowerSupplyController;

public static class SerialPortDiscovery
{
    private const string SerialCommRegistryPath = @"HARDWARE\DEVICEMAP\SERIALCOMM";

    public static IReadOnlyList<string> GetPortNames()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SerialCommRegistryPath);
            if (key is null)
            {
                return Array.Empty<string>();
            }

            return key.GetValueNames()
                .Select(name => key.GetValue(name)?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetPortOrder)
                .ToList()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int GetPortOrder(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return int.MaxValue;
        }

        string digits = new string(portName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int value) ? value : int.MaxValue;
    }
}
