using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public class QuickTextCollection : ObservableObject
{
    private string _name = "General";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "General";
    }

    public override string ToString() => Name;
}
