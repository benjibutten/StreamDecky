using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Rect = System.Windows.Rect;

namespace StreamDecky.Helpers;

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorStr && !string.IsNullOrEmpty(colorStr))
        {
            try
            {
                return new BrushConverter().ConvertFromString(colorStr) as Brush ?? Brushes.Transparent;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() ?? string.Empty;
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is true;
        bool invert = parameter is string s && s == "Invert";
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public class DoubleToCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return new System.Windows.CornerRadius(d);
        return new System.Windows.CornerRadius(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return 0.0;
    }
}

public class SpacingToThicknessConverter : IValueConverter
{
    public static readonly SpacingToThicknessConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return new Thickness(d / 2);
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return 0.0;
    }
}

public class HasTextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Empty;
    }
}

public class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 256;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Empty;
    }
}

public class OpacityConverter : IValueConverter
{
    public static readonly OpacityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? 1.0 : 0.35;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return true;
    }
}

public class ButtonShapeToGeometryConverter : IValueConverter
{
    private static readonly Dictionary<StreamDecky.Models.ButtonShape, string> ShapePaths = new()
    {
        [StreamDecky.Models.ButtonShape.Heart] = "M50,89 L44,83.5 C22.5,64 8,51 8,35 C8,22.5 18.5,12.5 31,12.5 C38.5,12.5 44,16 50,21 C56,16 61.5,12.5 69,12.5 C81.5,12.5 92,22.5 92,35 C92,51 77.5,64 56,83.5 L50,89 Z",
        [StreamDecky.Models.ButtonShape.Star] = "M50,0 L61,35 L98,35 L68,57 L79,91 L50,70 L21,91 L32,57 L2,35 L39,35 Z",
        [StreamDecky.Models.ButtonShape.Diamond] = "M50,0 L100,50 L50,100 L0,50 Z",
        [StreamDecky.Models.ButtonShape.Hexagon] = "M50,0 L93,25 L93,75 L50,100 L7,75 L7,25 Z",
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StreamDecky.Models.ButtonShape shape 
            && shape != StreamDecky.Models.ButtonShape.None 
            && ShapePaths.TryGetValue(shape, out var path))
        {
            try
            {
                return Geometry.Parse(path);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return StreamDecky.Models.ButtonShape.None;
    }
}

public class CellImageBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 4
            && values[0] is string path && !string.IsNullOrEmpty(path) && System.IO.File.Exists(path)
            && values[1] is int index
            && values[2] is int columns && columns > 0
            && values[3] is int rows && rows > 0)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                int col = index % columns;
                int row = index / columns;

                return new ImageBrush(bitmap)
                {
                    Viewbox = new Rect(
                        (double)col / columns,
                        (double)row / rows,
                        1.0 / columns,
                        1.0 / rows),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = Stretch.Fill
                };
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
