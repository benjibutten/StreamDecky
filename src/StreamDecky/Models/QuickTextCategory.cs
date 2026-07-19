using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public class QuickTextCategory : ObservableObject
{
    private string _name = "General";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }
    // Legacy schema-3 field. Tags are global from schema 4 onward.
    public string CollectionId { get; set; } = string.Empty;

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "General";

        CollectionId ??= string.Empty;
    }

    public override string ToString() => Name;
}
