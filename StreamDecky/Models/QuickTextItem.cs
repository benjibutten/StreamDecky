namespace StreamDecky.Models;

public class QuickTextItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CategoryId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        CategoryId ??= string.Empty;

        Text ??= string.Empty;
    }
}
