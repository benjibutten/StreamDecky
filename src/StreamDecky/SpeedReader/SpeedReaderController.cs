using System.ComponentModel;
using StreamDecky.Helpers;
using StreamDecky.ViewModels;
using StreamDecky.Views;

namespace StreamDecky.SpeedReader;

/// <summary>
/// Runs the speed reader hotkey: reads the selected text into the reader window, or
/// closes the reader when it is the window in front. Also opens the preview Settings
/// asks for, and keeps an open reader in step with the size and position settings.
/// </summary>
internal sealed class SpeedReaderController
{
    private const string PreviewText =
        "This is the speed reader. Drag the sliders in Settings to change its size and where it sits on the screen. "
        + "Press Space to pause, and Esc to close it.";

    private readonly MainViewModel _viewModel;
    private SpeedReaderWindow? _window;
    private bool _isCapturing;

    public SpeedReaderController(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.SpeedReaderPreviewRequested += (_, _) => ShowPreview();
    }

    public async Task HandleHotkeyAsync()
    {
        if (_window is { IsActive: true })
        {
            _window.Close();
            return;
        }

        // A second press while the copy is still in flight would copy again mid-way.
        if (_isCapturing)
            return;

        IntPtr foreground = OverlayInterop.GetCurrentForegroundWindow();
        string? text;
        _isCapturing = true;
        try
        {
            text = await SelectedTextCapture.CaptureAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Could not copy the selected text for the speed reader.", ex);
            text = null;
        }
        finally
        {
            _isCapturing = false;
        }

        // Pressing the hotkey without a new selection brings back the paused reader
        // rather than replacing the text with a hint.
        if (string.IsNullOrWhiteSpace(text) && _window is { HasWords: true })
        {
            _window.Resume(foreground);
            return;
        }

        _window ??= CreateWindow();
        _window.Read(text, foreground);
    }

    public void Close() => _window?.Close();

    private void ShowPreview()
    {
        _window ??= CreateWindow();
        _window.Read(PreviewText, OverlayInterop.GetCurrentForegroundWindow());
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SpeedReaderScalePercent) or nameof(MainViewModel.SpeedReaderVerticalPositionPercent))
            _window?.ApplyLayout(_viewModel.SpeedReaderScalePercent, _viewModel.SpeedReaderVerticalPositionPercent);
    }

    private SpeedReaderWindow CreateWindow()
    {
        var window = new SpeedReaderWindow(_viewModel.SpeedReaderWpm);
        window.ApplyLayout(_viewModel.SpeedReaderScalePercent, _viewModel.SpeedReaderVerticalPositionPercent);
        window.WpmChanged += wpm => _viewModel.SpeedReaderWpm = wpm;
        window.Closed += (_, _) => _window = null;
        return window;
    }
}
