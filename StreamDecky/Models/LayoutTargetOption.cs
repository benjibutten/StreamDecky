namespace StreamDecky.Models;

public class LayoutTargetOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public override string ToString() => Label;
}
