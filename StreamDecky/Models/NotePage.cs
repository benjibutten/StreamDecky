namespace StreamDecky.Models;

public class NotePage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Notes 1";
    public List<StickyNote> StickyNotes { get; set; } = new();

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "Notes";

        StickyNotes ??= new List<StickyNote>();
    }
}
