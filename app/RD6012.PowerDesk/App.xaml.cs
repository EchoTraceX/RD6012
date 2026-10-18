using System.Threading;
using System.Windows;

namespace RD6012.PowerDesk;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var mutex = new Mutex(true, @"Local\RD6012_PowerDesk", out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            MessageBox.Show(
                "RD6012 Power Desk 已经在运行。\n\n请切换到已打开的窗口。",
                "RD6012 Power Desk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _singleInstanceMutex = mutex;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}

