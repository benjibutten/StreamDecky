using System.Security.AccessControl;
using System.Threading;
using StreamDecky.Admin;
using StreamDecky.Helpers;
using StreamDecky.Services;
using StreamDecky.Updates;

namespace StreamDecky;

public static class Program
{
    private const string SingleInstanceMutexName = @"Local\StreamDecky.SingleInstance";

    /// <summary>Turns on "Run as administrator" and exits; the installer passes it when that box is ticked.</summary>
    private const string RunAsAdministratorArgument = "--run-as-administrator";

    // The duplicate-instance check runs before any WPF assembly is loaded or
    // App.xaml is parsed, so a second launch hands off and exits as fast as the
    // runtime allows instead of booting a whole application first.
    [STAThread]
    public static void Main(string[] args)
    {
        if (Elevation.IsElevated)
            Elevation.RefuseUntrustedJunctions();

        if (UpdateInstaller.IsUpdateMode(args))
        {
            RunApp();
            return;
        }

        if (UpdateInstaller.IsCleanupMode(args))
        {
            UpdateInstaller.RunCleanupAsync(args).GetAwaiter().GetResult();
            return;
        }

        if (args.Contains(RunAsAdministratorArgument, StringComparer.OrdinalIgnoreCase))
        {
            new AppSettingsService().RunAsAdministrator = true;
            return;
        }

        Elevation.WaitForPreviousInstance(args);

        Mutex mutex;
        bool isFirstInstance;
        try
        {
            mutex = MutexAcl.Create(true, SingleInstanceMutexName, out isFirstInstance, CreateMutexSecurity());
        }
        catch (UnauthorizedAccessException)
        {
            // Held by a StreamDecky running as administrator that does not grant this
            // account access. It is still running, so this launch must not become a
            // second instance.
            return;
        }

        using (mutex)
        {
            if (!isFirstInstance)
            {
                if (!App.HasStartHiddenInTrayArgument(args))
                    App.SignalRunningInstanceToActivate();
                return;
            }

            if (ShouldRestartAsAdministrator(args) && TryHandOverToElevatedCopy(args))
            {
                mutex.ReleaseMutex();
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
    }

    /// <summary>
    /// True when this launch should hand over to a copy running as administrator.
    /// Never at sign-in: a UAC prompt there would stand between the user and the desktop.
    /// </summary>
    private static bool ShouldRestartAsAdministrator(string[] args) =>
        !Elevation.IsElevated
        && Elevation.CanRunAsAdministrator
        && !App.HasStartHiddenInTrayArgument(args)
        && new AppSettingsService().RunAsAdministrator;

    private static bool TryHandOverToElevatedCopy(string[] args)
    {
        try
        {
            // The copy takes over this launch, including an update's --update-cleanup.
            if (Elevation.TryStartElevatedCopy(args))
                return true;

            // Declined: leaving the setting on would ask again at every launch.
            new AppSettingsService().RunAsAdministrator = false;
            return false;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Could not restart StreamDecky as administrator; running without administrator rights.", ex);
            return false;
        }
    }

    // The instance that creates the mutex may run as administrator, and then Windows
    // gives it a default permission that shuts every process without administrator
    // rights out, including a second StreamDecky launched from the Start menu.
    private static MutexSecurity CreateMutexSecurity()
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(Elevation.CurrentUser(), MutexRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
