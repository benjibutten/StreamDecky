using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    private readonly System.Timers.Timer _autoSaveTimer;
    private readonly SemaphoreSlim _autoSaveSemaphore = new(1, 1);
    private readonly CancellationTokenSource _autoSaveCancellation = new();
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
        if (_isDisposed)
            return;

        MarkUnsavedChanges();
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async Task AutoSaveAsync()
    {
        bool hasAutoSaveLock = false;

        try
        {
            CancellationToken cancellationToken = _autoSaveCancellation.Token;
            await _autoSaveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            hasAutoSaveLock = true;

            long versionToSave = Interlocked.Read(ref _changeVersion);
            await InvokeOnUiThreadAsync(BeginSaveStatus, cancellationToken).ConfigureAwait(false);

            string json;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                json = await dispatcher.InvokeAsync(
                    () => _profileService.SerializeStore(_profileStore),
                    System.Windows.Threading.DispatcherPriority.Normal,
                    cancellationToken);
            }
            else
            {
                json = _profileService.SerializeStore(_profileStore);
            }

            await _profileService.SaveStoreSerializedAsync(json, cancellationToken).ConfigureAwait(false);
            await InvokeOnUiThreadAsync(() => CompleteSaveStatus(versionToSave), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_autoSaveCancellation.IsCancellationRequested)
        {
            // Application shutdown cancels queued/in-flight autosaves before the
            // final synchronous snapshot is written from Dispose().
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Autosave failed.", ex);
            if (!_autoSaveCancellation.IsCancellationRequested)
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

    /// <summary>Persists the current profile snapshot without the normal debounce.
    /// Used when another durable write depends on profile state already being
    /// committed, such as reserving the next form counter value.</summary>
    private async Task<bool> SavePendingChangesImmediatelyAsync()
    {
        if (_isDisposed)
            return false;

        _autoSaveTimer.Stop();
        MarkUnsavedChanges();
        long versionToSave = Interlocked.Read(ref _changeVersion);
        BeginSaveStatus();
        string json;
        try
        {
            json = _profileService.SerializeStore(_profileStore);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Immediate profile serialization failed.", ex);
            FailSaveStatus(ex);
            return false;
        }

        bool hasSaveLock = false;
        try
        {
            CancellationToken cancellationToken = _autoSaveCancellation.Token;
            await _autoSaveSemaphore.WaitAsync(cancellationToken);
            hasSaveLock = true;
            await _profileService.SaveStoreSerializedAsync(json, cancellationToken);
            CompleteSaveStatus(versionToSave);
            return true;
        }
        catch (OperationCanceledException) when (_autoSaveCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Immediate profile save failed.", ex);
            FailSaveStatus(ex);
            return false;
        }
        finally
        {
            if (hasSaveLock)
                _autoSaveSemaphore.Release();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_isDisposed)
            return;

        CancellationToken cancellationToken = _autoSaveCancellation.Token;
        long versionToSave = Interlocked.Read(ref _changeVersion);
        BeginSaveStatus();
        bool hasSaveLock = false;

        try
        {
            string json = _profileService.SerializeStore(_profileStore);
            await _autoSaveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            hasSaveLock = true;
            await _profileService.SaveStoreSerializedAsync(json, cancellationToken).ConfigureAwait(false);
            await InvokeOnUiThreadAsync(
                () => CompleteSaveStatus(versionToSave),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_autoSaveCancellation.IsCancellationRequested)
        {
            // Shutdown cancels the manual save before Dispose() writes the final
            // synchronous snapshot. Do not queue any continuation back to the UI.
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Manual save failed.", ex);
            if (!_autoSaveCancellation.IsCancellationRequested)
                await InvokeOnUiThreadAsync(() => FailSaveStatus(ex)).ConfigureAwait(false);
        }
        finally
        {
            if (hasSaveLock)
                _autoSaveSemaphore.Release();
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

    private static Task InvokeOnUiThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(
            action,
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken).Task;
    }
}
