using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace StreamDecky.Views;

public partial class ColorPickerDialog : Window
{
    private static readonly string[] PresetColors =
    [
        "#6C5CE7", "#A29BFE", "#0984E3", "#74B9FF", "#00CEC9", "#00B894", "#55EFC4", "#2D7A4F", "#FDCB6E", "#E17055",
        "#D63031", "#FF7675", "#E84393", "#FD79A8", "#B2BEC3", "#636E72", "#2D3436", "#1E1E2E", "#000000", "#FFFFFF"
    ];

    private const int MaxRecentColors = 10;

    // Kept for the lifetime of the app only, which covers building several buttons in a row.
    private static readonly List<string> RecentColorHexes = new();

    private double _hue;
    private double _saturation;
    private double _value;
    private bool _isWritingHex;

    private ColorPickerDialog(string currentHex)
    {
        InitializeComponent();

        foreach (string recent in RecentColorHexes)
            RecentColors.Children.Add(CreateSwatch(recent));
        RecentSection.Visibility = RecentColorHexes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (string preset in PresetColors)
            Presets.Children.Add(CreateSwatch(preset));

        Color start = TryParse(currentHex) ?? Color.FromRgb(0x6C, 0x5C, 0xE7);
        OriginalSwatch.Background = new SolidColorBrush(start);
        SelectColor(start);

        Loaded += (_, _) =>
        {
            HexBox.Focus();
            HexBox.SelectAll();
        };
    }

    public string SelectedHex => ToHex(CurrentColor);

    private Color CurrentColor => FromHsv(_hue, _saturation, _value);

    /// <summary>
    /// Shows the picker over <paramref name="owner"/>, starting from <paramref name="currentHex"/>
    /// (or the accent colour when it cannot be parsed).
    /// </summary>
    /// <returns>The chosen colour as #RRGGBB, or null when the dialog was cancelled.</returns>
    public static string? Show(Window? owner, string currentHex)
    {
        var dialog = new ColorPickerDialog(currentHex) { Owner = owner };
        if (owner == null)
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
    }

    private Border CreateSwatch(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var swatch = new Border
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 0, 5, 5),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = hex
        };
        swatch.MouseLeftButtonDown += (_, _) => SelectColor(color);
        return swatch;
    }

    private void SelectColor(Color color)
    {
        (_hue, _saturation, _value) = ToHsv(color, _hue);
        Refresh(writeHex: true);
    }

    private void Refresh(bool writeHex)
    {
        Color color = CurrentColor;
        HueLayer.Fill = new SolidColorBrush(FromHsv(_hue, 1, 1));
        NewSwatch.Background = new SolidColorBrush(color);

        Canvas.SetLeft(SaturationValueMarker, _saturation * SaturationValueArea.Width - SaturationValueMarker.Width / 2);
        Canvas.SetTop(SaturationValueMarker, (1 - _value) * SaturationValueArea.Height - SaturationValueMarker.Height / 2);
        Canvas.SetTop(HueMarker, _hue / 360 * HueArea.Height - HueMarker.Height / 2);

        if (!writeHex)
            return;

        _isWritingHex = true;
        HexBox.Text = ToHex(color);
        _isWritingHex = false;
    }

    private void SaturationValue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).CaptureMouse();
        PickSaturationValue(e);
    }

    private void SaturationValue_MouseMove(object sender, MouseEventArgs e)
    {
        if (((UIElement)sender).IsMouseCaptured)
            PickSaturationValue(e);
    }

    private void PickSaturationValue(MouseEventArgs e)
    {
        Point point = e.GetPosition(SaturationValueArea);
        _saturation = Math.Clamp(point.X / SaturationValueArea.Width, 0, 1);
        _value = 1 - Math.Clamp(point.Y / SaturationValueArea.Height, 0, 1);
        Refresh(writeHex: true);
    }

    private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).CaptureMouse();
        PickHue(e);
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (((UIElement)sender).IsMouseCaptured)
            PickHue(e);
    }

    private void PickHue(MouseEventArgs e)
    {
        // Kept just below 360, which would wrap round to red at the top.
        _hue = Math.Clamp(e.GetPosition(HueArea).Y / HueArea.Height, 0, 0.9999) * 360;
        Refresh(writeHex: true);
    }

    private void Area_MouseUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isWritingHex || TryParse(HexBox.Text.Trim()) is not { } color)
            return;

        // Rewriting the box here would move the caret while the user types.
        (_hue, _saturation, _value) = ToHsv(color, _hue);
        Refresh(writeHex: false);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        DialogResult = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        RememberRecent(SelectedHex);
        DialogResult = true;
    }

    private static void RememberRecent(string hex)
    {
        RecentColorHexes.RemoveAll(existing => string.Equals(existing, hex, StringComparison.OrdinalIgnoreCase));
        RecentColorHexes.Insert(0, hex);
        if (RecentColorHexes.Count > MaxRecentColors)
            RecentColorHexes.RemoveRange(MaxRecentColors, RecentColorHexes.Count - MaxRecentColors);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static Color? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string candidate = text.StartsWith('#') ? text : "#" + text;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(candidate);
            return Color.FromRgb(color.R, color.G, color.B);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <param name="previousHue">Kept for greys, whose hue is undefined, so the hue marker stays put.</param>
    private static (double Hue, double Saturation, double Value) ToHsv(Color color, double previousHue)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = previousHue;
        if (delta > 0)
        {
            if (max == r)
                hue = 60 * ((g - b) / delta % 6);
            else if (max == g)
                hue = 60 * ((b - r) / delta + 2);
            else
                hue = 60 * ((r - g) / delta + 4);

            if (hue < 0)
                hue += 360;
        }

        double saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;

        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
