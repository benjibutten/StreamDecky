using System.Text.Json.Serialization;

namespace StreamDecky.Models;

public class ButtonConfig
{
    public string Title { get; set; } = string.Empty;
    public ActionType ActionType { get; set; } = ActionType.None;
    public string BackgroundColor { get; set; } = "#3C3C3C";
    public string TextColor { get; set; } = "#FFFFFF";
    public string IconText { get; set; } = string.Empty;
    public double CornerRadius { get; set; } = 8;
    public string ImagePath { get; set; } = string.Empty;

    // Text Input action properties
    public string Text { get; set; } = string.Empty;
    public bool PressEnterAfter { get; set; }
    public TextMode TextMode { get; set; } = TextMode.PasteFromClipboard;

    // Key Press action properties
    public string KeyText { get; set; } = string.Empty;

    // Multi-Action properties
    public List<ActionStep> Steps { get; set; } = new();

    // Layout navigation action properties
    public string TargetLayoutId { get; set; } = string.Empty;

    // Shape
    public ButtonShape Shape { get; set; } = ButtonShape.None;
}
