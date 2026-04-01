namespace StreamDecky.Models;

public class DeckPage
{
    public string Name { get; set; } = "Page 1";
    public int Rows { get; set; } = 3;
    public int Columns { get; set; } = 5;
    public List<ButtonConfig> Buttons { get; set; } = new();

    public void EnsureButtonCount()
    {
        int total = Rows * Columns;
        while (Buttons.Count < total)
            Buttons.Add(new ButtonConfig());
        if (Buttons.Count > total)
            Buttons.RemoveRange(total, Buttons.Count - total);
    }
}
