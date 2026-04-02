using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class StickyNoteViewModel : ObservableObject
{
    private readonly StickyNote _model;
    private readonly Action _onChanged;

    public StickyNoteViewModel(StickyNote model, Action onChanged)
    {
        _model = model;
        _onChanged = onChanged;
    }

    public StickyNote Model => _model;
    public string Id => _model.Id;

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
            double clamped = Math.Clamp(value, 160, 420);
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
            double clamped = Math.Clamp(value, 120, 360);
            if (Math.Abs(_model.Height - clamped) < 0.1)
                return;

            _model.Height = clamped;
            OnPropertyChanged();
            _onChanged();
        }
    }

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
