using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.ViewModels;

public partial class OverlayQuickTextSessionItemViewModel : ObservableObject
{
    public OverlayQuickTextSessionItemViewModel(string id, string sessionText, string categoryName = "")
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        _sessionText = sessionText ?? string.Empty;
        CategoryName = categoryName ?? string.Empty;
    }

    public string Id { get; }
    public string CategoryName { get; }

    [ObservableProperty]
    private string _sessionText;

    [ObservableProperty]
    private bool _isEditing;

    public string PreviewText => string.IsNullOrWhiteSpace(SessionText) ? "(Empty text)" : SessionText;

    partial void OnSessionTextChanged(string value)
    {
        OnPropertyChanged(nameof(PreviewText));
    }
}
