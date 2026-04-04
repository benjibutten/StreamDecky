namespace StreamDecky.Models;

public class QuickTextCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "General";

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "General";
    }

    public override string ToString() => Name;
}
