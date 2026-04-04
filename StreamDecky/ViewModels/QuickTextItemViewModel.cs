using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class QuickTextItemViewModel : ObservableObject
{
    private readonly QuickTextItem _model;
    private readonly Action _onChanged;

    public QuickTextItemViewModel(QuickTextItem model, Action onChanged)
    {
        _model = model;
        _onChanged = onChanged;
    }

    public QuickTextItem Model => _model;
    public string Id => _model.Id;

    public string Text
    {
        get => _model.Text;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_model.Text, normalized, StringComparison.Ordinal))
                return;

            _model.Text = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewText));
            _onChanged();
        }
    }

    public string PreviewText => string.IsNullOrWhiteSpace(_model.Text) ? "(Empty text)" : _model.Text;
}
