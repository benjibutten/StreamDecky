namespace StreamDecky.Models;

public class DeckProfile
{
    public string Name { get; set; } = "Default";
    public string OverlayBackgroundColor { get; set; } = "#1E1E2E";
    public string OverlayBackgroundImagePath { get; set; } = string.Empty;
    public double ButtonOverlayOpacity { get; set; } = 1.0;
    public double ButtonSpacing { get; set; } = 10;
    public double ButtonSize { get; set; } = 100;
    public double GridOffsetX { get; set; } = 0;
    public double GridOffsetY { get; set; } = 0;
    public uint HotkeyModifiers { get; set; } = 0x0002; // MOD_CONTROL
    public uint HotkeyVk { get; set; } = 0x7B;          // VK_F12
    public string HotkeyDisplayText { get; set; } = "Ctrl + F12";
    public bool StartWithWindows { get; set; } = false;
    public List<DeckPage> Pages { get; set; } = new() { new DeckPage() };

    public void Initialize()
    {
        ButtonOverlayOpacity = Math.Clamp(ButtonOverlayOpacity, 0.2, 1.0);

        if (Pages.Count == 0)
            Pages.Add(new DeckPage());

        foreach (var page in Pages)
            page.EnsureButtonCount();
    }
}
