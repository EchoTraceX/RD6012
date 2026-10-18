using System.IO;
using System.Reflection;

namespace PowerSupplyController;

internal static class DiagnosticLogger
{
    private static readonly object SyncRoot = new();

    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RD6012_PowerSupplyController",
            "diagnostic.log");

    public static void Log(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [PID {Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void LogProcessStart(string source)
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        Log($"{source} | entry={assembly.Location} | baseDir={AppContext.BaseDirectory}");
    }
}
