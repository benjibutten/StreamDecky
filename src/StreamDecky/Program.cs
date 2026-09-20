using System.Threading;
using StreamDecky.Updates;

namespace StreamDecky;

public static class Program
{
    private const string SingleInstanceMutexName = @"Local\StreamDecky.SingleInstance";

    // The duplicate-instance check runs before any WPF assembly is loaded or
    // App.xaml is parsed, so a second launch hands off and exits as fast as the
    // runtime allows instead of booting a whole application first.
    [STAThread]
    public static void Main(string[] args)
    {
        if (UpdateInstaller.IsUpdateMode(args))
        {
            RunApp();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (!App.HasStartHiddenInTrayArgument(args))
                App.SignalRunningInstanceToActivate();
            return;
        }

        try
        {
            RunApp();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
