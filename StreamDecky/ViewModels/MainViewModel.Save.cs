using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    private readonly System.Timers.Timer _autoSaveTimer;
    private readonly SemaphoreSlim _autoSaveSemaphore = new(1, 1);
    private long _changeVersion;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _isSaveInProgress;

    [ObservableProperty]
    private bool _hasSaveError;

    [ObservableProperty]
    private string _saveStatusText = "All changes saved";

    [ObservableProperty]
    private string _saveStatusDetails = "No pending changes.";

    [ObservableProperty]
    private string _saveStatusColor = "#A6E3A1";

    private void ScheduleAutoSave()
    {
        MarkUnsavedChanges();
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async Task AutoSaveAsync()
    {
        bool hasAutoSaveLock = false;

        try
        {
            await _autoSaveSemaphore.WaitAsync().ConfigureAwait(false);
            hasAutoSaveLock = true;

            long versionToSave = Interlocked.Read(ref _changeVersion);
            await InvokeOnUiThreadAsync(BeginSaveStatus).ConfigureAwait(false);

            string json;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                json = await dispatcher.InvokeAsync(() => _profileService.SerializeStore(_profileStore));
            }
            else
            {
                json = _profileService.SerializeStore(_profileStore);
            }

            await _profileService.SaveStoreSerializedAsync(json).ConfigureAwait(false);
            await InvokeOnUiThreadAsync(() => CompleteSaveStatus(versionToSave)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Autosave failed.", ex);
            await InvokeOnUiThreadAsync(() => FailSaveStatus(ex)).ConfigureAwait(false);
        }
        finally
        {
            if (hasAutoSaveLock)
            {
                try
                {
                    _autoSaveSemaphore.Release();
                }
                catch
                {
                    // Ignore release errors during application shutdown.
                }
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        long versionToSave = Interlocked.Read(ref _changeVersion);
        BeginSaveStatus();

        try
        {
            _profileService.SaveStore(_profileStore);
            CompleteSaveStatus(versionToSave);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Manual save failed.", ex);
            FailSaveStatus(ex);
        }
    }

    private void MarkUnsavedChanges()
    {
        Interlocked.Increment(ref _changeVersion);
        HasUnsavedChanges = true;

        if (HasSaveError)
            return;

        if (IsSaveInProgress)
            return;

        SaveStatusText = "Unsaved changes";
        SaveStatusDetails = "Changes are waiting to be saved.";
        SaveStatusColor = "#EBCB8B";
    }

    private void BeginSaveStatus()
    {
        IsSaveInProgress = true;
        HasSaveError = false;
        SaveStatusText = "Saving...";
        SaveStatusDetails = "Saving changes to the profile store.";
        SaveStatusColor = "#89B4FA";
    }

    private void CompleteSaveStatus(long savedVersion)
    {
        IsSaveInProgress = false;

        long currentVersion = Interlocked.Read(ref _changeVersion);
        if (savedVersion == currentVersion)
        {
            HasUnsavedChanges = false;
            HasSaveError = false;
            SaveStatusText = $"Saved {DateTime.Now:HH:mm:ss}";
            SaveStatusDetails = "All changes saved.";
            SaveStatusColor = "#A6E3A1";
            return;
        }

        HasUnsavedChanges = true;
        SaveStatusText = "Unsaved changes";
        SaveStatusDetails = "New changes were made while the previous save completed.";
        SaveStatusColor = "#EBCB8B";
    }

    private void FailSaveStatus(Exception ex)
    {
        IsSaveInProgress = false;
        HasUnsavedChanges = true;
        HasSaveError = true;
        SaveStatusText = "Save failed";
        SaveStatusDetails = ex.Message;
        SaveStatusColor = "#F38BA8";
    }

    private static Task InvokeOnUiThreadAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}