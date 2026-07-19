using System.Windows;
using System.Windows.Input;
using StreamDecky.Helpers;
using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;

namespace StreamDecky.Updates;

internal static class UpdateCoordinator
{
    private static readonly GitHubUpdateService Service = new();
    private static int _checkInProgress;

    public static async Task CheckAsync(Window owner, bool manual)
    {
        Version? currentVersion = AppVersion.Current;
        if (currentVersion is null || currentVersion.Major < 2000)
        {
            if (manual)
                MessageBox.Show(owner, "Update checks are only available in release builds.", "StreamDecky Update", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _checkInProgress, 1) != 0)
            return;

        try
        {
            UpdateInfo? update = await Service.CheckAsync(currentVersion, manual);
            if (update is null)
            {
                if (manual)
                    MessageBox.Show(owner, "You already have the latest version.", "StreamDecky Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                owner,
                $"StreamDecky {update.TagName} is available. You have {AppVersion.DisplayText}.\n\nDownload and install it now? StreamDecky will close and restart automatically.\n\nWindows may request UAC approval or show a security warning when the updated app restarts, especially for unsigned or newly published builds.",
                "StreamDecky Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
                return;

            Mouse.OverrideCursor = Cursors.Wait;
            owner.IsEnabled = false;
            try
            {
                await Service.LaunchInstallerAsync(update);
                if (Application.Current.MainWindow is MainWindow mainWindow)
                    mainWindow.ExitForUpdate();
                else
                    Application.Current.Shutdown();
            }
            finally
            {
                owner.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Update check or installation preparation failed.", ex);
            if (manual)
                MessageBox.Show(owner, $"Could not check for or prepare the update.\n\n{ex.Message}", "StreamDecky Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Interlocked.Exchange(ref _checkInProgress, 0);
        }
    }
}
