namespace StreamDecky.Models;

public class ProfileOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public override string ToString() => Label;
}
