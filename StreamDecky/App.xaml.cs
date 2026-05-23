using System.Threading;
using System.Windows;

using Application = System.Windows.Application;

namespace StreamDecky;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\StreamDecky.SingleInstance";
    private const string ActivateExistingInstanceEventName = @"Local\StreamDecky.ActivateExistingInstance";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateExistingInstanceEvent;
    private RegisteredWaitHandle? _activationWaitHandle;
    private bool _ownsSingleInstanceMutex;
    private bool _activateMainWindowWhenReady;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool startHiddenInTray = HasStartHiddenInTrayArgument(e.Args);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;

        if (!isFirstInstance)
        {
            if (!startHiddenInTray)
                SignalRunningInstanceToActivate();

            Shutdown();
            return;
        }

        _activateExistingInstanceEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateExistingInstanceEventName);

        _activationWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _activateExistingInstanceEvent,
            static (state, _) =>
            {
                if (state is App app && !app.Dispatcher.HasShutdownStarted)
                    app.Dispatcher.BeginInvoke(app.ActivateMainWindow);
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);

        base.OnStartup(e);
        CreateMainWindow(startHiddenInTray);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWaitHandle?.Unregister(null);
        _activationWaitHandle = null;

        _activateExistingInstanceEvent?.Dispose();
        _activateExistingInstanceEvent = null;

        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    private static void SignalRunningInstanceToActivate()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivateExistingInstanceEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance is still starting and has not created its activation signal yet.
        }
    }

    private static bool HasStartHiddenInTrayArgument(string[] args)
    {
        return args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
    }

    private void CreateMainWindow(bool startHiddenInTray)
    {
        var mainWindow = new MainWindow(startHiddenInTray);
        MainWindow = mainWindow;

        if (_activateMainWindowWhenReady)
        {
            _activateMainWindowWhenReady = false;
            mainWindow.ShowAndActivate();
            return;
        }

        if (startHiddenInTray)
        {
            mainWindow.StartHiddenInTray();
            return;
        }

        mainWindow.Show();
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.ShowAndActivate();
            return;
        }

        if (MainWindow == null)
        {
            _activateMainWindowWhenReady = true;
            return;
        }

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;

        MainWindow.Activate();
    }
}

