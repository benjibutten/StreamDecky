using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public partial class FormCounter : ObservableObject
{
    public const int MinPadWidth = 0;
    public const int MaxPadWidth = 10;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Token name used as {Name} in templates and field defaults.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>The value the next submission will use. Every counter on a
    /// template increments once per successful submission.</summary>
    [ObservableProperty]
    private int _nextValue = 1;

    /// <summary>Zero-pads the formatted value to this width; 0 disables padding.</summary>
    [ObservableProperty]
    private int _padWidth;

    /// <summary>Live "shows as" sample for the editor, e.g. 003 for value 3, digits 3.</summary>
    public string FormattedPreview => FormatValue();

    partial void OnNextValueChanged(int value) => OnPropertyChanged(nameof(FormattedPreview));

    partial void OnPadWidthChanged(int value) => OnPropertyChanged(nameof(FormattedPreview));

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Name = FormField.NormalizeKey(Name);
        PadWidth = Math.Clamp(PadWidth, MinPadWidth, MaxPadWidth);
    }

    public string FormatValue()
    {
        return PadWidth > 0
            ? NextValue.ToString(new string('0', PadWidth))
            : NextValue.ToString();
    }
}
