namespace StreamDecky.Models;

public class StickyNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public double X { get; set; } = 96;
    public double Y { get; set; } = 140;
    public double Width { get; set; } = 230;
    public double Height { get; set; } = 180;
    public string Color { get; set; } = "#F8E784";
}
