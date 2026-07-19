using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    private MusicWidgetViewModel? _musicWidget;

    /// <summary>Created lazily so sessions that never show the widget pay no
    /// pipe-client cost; the reconnect loop only starts on <see cref="MusicWidgetViewModel.Activate"/>.</summary>
    public MusicWidgetViewModel MusicWidget => _musicWidget ??= new MusicWidgetViewModel();

    public bool MusicWidgetVisible
    {
        get => _profile.MusicWidgetVisible;
        set
        {
            if (_profile.MusicWidgetVisible == value)
                return;

            _profile.MusicWidgetVisible = value;
            OnPropertyChanged();
            ScheduleAutoSave();

            if (value && IsOverlayOpen)
                MusicWidget.Activate();
        }
    }

    public bool MusicWidgetMinimized
    {
        get => _profile.MusicWidgetMinimized;
        set
        {
            if (_profile.MusicWidgetMinimized == value)
                return;

            _profile.MusicWidgetMinimized = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double MusicWidgetX
    {
        get => _profile.MusicWidgetX;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.MusicWidgetX - clamped) < 0.001)
                return;

            _profile.MusicWidgetX = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double MusicWidgetY
    {
        get => _profile.MusicWidgetY;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.MusicWidgetY - clamped) < 0.001)
                return;

            _profile.MusicWidgetY = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double MusicWidgetWidth
    {
        get => _profile.MusicWidgetWidth;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinMusicWidgetWidth, DeckProfile.MaxMusicWidgetWidth);
            if (Math.Abs(_profile.MusicWidgetWidth - clamped) < 0.001)
                return;

            _profile.MusicWidgetWidth = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double MusicWidgetHeight
    {
        get => _profile.MusicWidgetHeight;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinMusicWidgetHeight, DeckProfile.MaxMusicWidgetHeight);
            if (Math.Abs(_profile.MusicWidgetHeight - clamped) < 0.001)
                return;

            _profile.MusicWidgetHeight = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    [RelayCommand]
    private void ToggleMusicWidget()
    {
        MusicWidgetVisible = !MusicWidgetVisible;
    }

    [RelayCommand]
    private void ToggleMusicWidgetMinimized()
    {
        MusicWidgetMinimized = !MusicWidgetMinimized;
    }

    [RelayCommand]
    private void HideMusicWidget()
    {
        MusicWidgetVisible = false;
    }

    private void ActivateMusicWidgetIfVisible()
    {
        if (MusicWidgetVisible)
            MusicWidget.Activate();
    }
}
