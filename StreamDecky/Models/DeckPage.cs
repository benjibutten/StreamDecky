namespace StreamDecky.Models;

public class DeckPage
{
    public const int MinRows = 1;
    public const int MaxRows = 10;
    public const int MinColumns = 1;
    public const int MaxColumns = 10;
    public const int MaxButtonsPerPage = MaxRows * MaxColumns;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Page 1";
    public int Rows { get; set; } = 3;
    public int Columns { get; set; } = 5;
    public List<ButtonConfig> Buttons { get; set; } = new();
    // Legacy field kept for profile backward compatibility. Notes now live in DeckProfile.NotePages.
    public List<StickyNote> StickyNotes { get; set; } = new();

    public void EnsureButtonCount(int? rows = null, int? columns = null)
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Rows = Math.Clamp(rows ?? Rows, MinRows, MaxRows);
        Columns = Math.Clamp(columns ?? Columns, MinColumns, MaxColumns);
        StickyNotes ??= new List<StickyNote>();

        int total = Rows * Columns;
        if (total > MaxButtonsPerPage)
            total = MaxButtonsPerPage;

        while (Buttons.Count < total)
            Buttons.Add(new ButtonConfig());
        if (Buttons.Count > total)
            Buttons.RemoveRange(total, Buttons.Count - total);
    }
}
