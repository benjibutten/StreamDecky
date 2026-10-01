namespace StreamDecky.SpeedReader;

/// <summary>
/// Machine-wide speed reader settings, stored in their own file by
/// <see cref="SpeedReaderSettingsStore"/>.
/// </summary>
public sealed class SpeedReaderSettings
{
    public const int MinWpm = 100;
    public const int MaxWpm = 1000;
    public const int WpmStep = 25;
    public const int DefaultWpm = 400;

    // Ctrl+Alt+R: free in browsers and editors, unlike Ctrl+Shift+R (hard reload).
    public const uint DefaultHotkeyModifiers = 0x0003; // MOD_ALT | MOD_CONTROL
    public const uint DefaultHotkeyVk = 0x52;          // R
    public const string DefaultHotkeyDisplayText = "Ctrl + Alt + R";

    public const int MinScalePercent = 60;
    public const int MaxScalePercent = 160;

    // Where the reader's centre sits, from the top of the screen.
    public const int MinVerticalPositionPercent = 10;
    public const int MaxVerticalPositionPercent = 90;

    public int Wpm { get; set; } = DefaultWpm;

    public int ScalePercent { get; set; } = 100;

    public int VerticalPositionPercent { get; set; } = 50;

    public uint HotkeyModifiers { get; set; } = DefaultHotkeyModifiers;

    public uint HotkeyVk { get; set; } = DefaultHotkeyVk;

    public string HotkeyDisplayText { get; set; } = DefaultHotkeyDisplayText;

    public static int ClampWpm(int wpm) => Math.Clamp(wpm, MinWpm, MaxWpm);

    public static int ClampScalePercent(int percent) => Math.Clamp(percent, MinScalePercent, MaxScalePercent);

    public static int ClampVerticalPositionPercent(int percent) =>
        Math.Clamp(percent, MinVerticalPositionPercent, MaxVerticalPositionPercent);

    public void Normalize()
    {
        Wpm = ClampWpm(Wpm);
        ScalePercent = ClampScalePercent(ScalePercent);
        VerticalPositionPercent = ClampVerticalPositionPercent(VerticalPositionPercent);

        if (HotkeyVk == 0 || string.IsNullOrWhiteSpace(HotkeyDisplayText))
        {
            HotkeyModifiers = DefaultHotkeyModifiers;
            HotkeyVk = DefaultHotkeyVk;
            HotkeyDisplayText = DefaultHotkeyDisplayText;
        }
    }
}
