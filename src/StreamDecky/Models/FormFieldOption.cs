using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public partial class FormFieldOption : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Label ??= string.Empty;
        Text ??= string.Empty;
    }
}
