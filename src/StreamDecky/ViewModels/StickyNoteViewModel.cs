using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class StickyNoteViewModel : ObservableObject
{
    public const double MinimizedHeight = 28;
    public const double MinWidth = 160;
    public const double MaxWidth = 420;
    public const double MinHeight = 120;
    public const double MaxHeight = 360;

    [ObservableProperty]
    private bool _isEditingTitle;

    private readonly StickyNote _model;
    private readonly Action _onChanged;

    public StickyNoteViewModel(StickyNote model, Action onChanged)
    {
        _model = model;
        _onChanged = onChanged;
    }

    public StickyNote Model => _model;
    public string Id => _model.Id;

    public string Title
    {
        get => string.IsNullOrWhiteSpace(_model.Title) ? "Sticky note" : _model.Title;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "Sticky note" : value.Trim();
            if (string.Equals(_model.Title, normalized, StringComparison.Ordinal))
                return;

            _model.Title = normalized;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Text
    {
        get => _model.Text;
        set
        {
            if (_model.Text == value)
                return;

            _model.Text = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double X
    {
        get => _model.X;
        set
        {
            if (Math.Abs(_model.X - value) < 0.1)
                return;

            _model.X = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double Y
    {
        get => _model.Y;
        set
        {
            if (Math.Abs(_model.Y - value) < 0.1)
                return;

            _model.Y = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double Width
    {
        get => _model.Width;
        set
        {
            double clamped = Math.Clamp(value, MinWidth, MaxWidth);
            if (Math.Abs(_model.Width - clamped) < 0.1)
                return;

            _model.Width = clamped;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double Height
    {
        get => _model.Height;
        set
        {
            double clamped = Math.Clamp(value, MinHeight, MaxHeight);
            if (Math.Abs(_model.Height - clamped) < 0.1)
                return;

            _model.Height = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayHeight));
            _onChanged();
        }
    }

    public bool IsMinimized
    {
        get => _model.IsMinimized;
        set
        {
            if (_model.IsMinimized == value)
                return;

            _model.IsMinimized = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayHeight));
            _onChanged();
        }
    }

    public double DisplayHeight => IsMinimized ? MinimizedHeight : Height;

    public string Color
    {
        get => _model.Color;
        set
        {
            if (_model.Color == value)
                return;

            _model.Color = value;
            OnPropertyChanged();
            _onChanged();
        }
    }
}
