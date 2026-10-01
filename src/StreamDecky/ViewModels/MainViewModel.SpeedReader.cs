using StreamDecky.SpeedReader;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    private readonly SpeedReaderSettingsStore _speedReaderStore;
    private readonly SpeedReaderSettings _speedReaderSettings;

    /// <summary>Machine-wide, like <see cref="RunAsAdministrator"/>.</summary>
    public int SpeedReaderWpm
    {
        get => _speedReaderSettings.Wpm;
        set
        {
            int wpm = SpeedReaderSettings.ClampWpm(value);
            if (_speedReaderSettings.Wpm == wpm)
                return;

            _speedReaderSettings.Wpm = wpm;
            _speedReaderStore.Save(_speedReaderSettings);
            OnPropertyChanged();
        }
    }

    public int SpeedReaderScalePercent
    {
        get => _speedReaderSettings.ScalePercent;
        set
        {
            int percent = SpeedReaderSettings.ClampScalePercent(value);
            if (_speedReaderSettings.ScalePercent == percent)
                return;

            _speedReaderSettings.ScalePercent = percent;
            _speedReaderStore.Save(_speedReaderSettings);
            OnPropertyChanged();
        }
    }

    public int SpeedReaderVerticalPositionPercent
    {
        get => _speedReaderSettings.VerticalPositionPercent;
        set
        {
            int percent = SpeedReaderSettings.ClampVerticalPositionPercent(value);
            if (_speedReaderSettings.VerticalPositionPercent == percent)
                return;

            _speedReaderSettings.VerticalPositionPercent = percent;
            _speedReaderStore.Save(_speedReaderSettings);
            OnPropertyChanged();
        }
    }

    /// <summary>Raised by <see cref="ShowSpeedReaderPreview"/>.</summary>
    public event EventHandler? SpeedReaderPreviewRequested;

    /// <summary>Asks for the reader to open on sample text, so size and position can be judged live.</summary>
    public void ShowSpeedReaderPreview() => SpeedReaderPreviewRequested?.Invoke(this, EventArgs.Empty);

    public uint SpeedReaderHotkeyModifiers => _speedReaderSettings.HotkeyModifiers;

    public uint SpeedReaderHotkeyVk => _speedReaderSettings.HotkeyVk;

    public string SpeedReaderHotkeyDisplayText => _speedReaderSettings.HotkeyDisplayText;

    /// <summary>
    /// Saves a new speed reader hotkey. Raises <see cref="SpeedReaderHotkeyVk"/> changed
    /// even when only the modifiers differ, so listeners re-register on that one property.
    /// </summary>
    public void SetSpeedReaderHotkey(uint modifiers, uint vk, string displayText)
    {
        _speedReaderSettings.HotkeyModifiers = modifiers;
        _speedReaderSettings.HotkeyVk = vk;
        _speedReaderSettings.HotkeyDisplayText = displayText;
        _speedReaderStore.Save(_speedReaderSettings);

        OnPropertyChanged(nameof(SpeedReaderHotkeyDisplayText));
        OnPropertyChanged(nameof(SpeedReaderHotkeyVk));
    }
}
