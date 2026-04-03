using System.Globalization;
using System.Collections;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using StreamDecky.ViewModels;

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
    private const int MaxImageCacheEntries = 192;
    private static readonly Dictionary<string, ImageSource> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LinkedListNode<string>> ImageCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ImageCacheOrder = new();
    private static readonly object ImageCacheLock = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try
            {
                int decodeWidth = 0;
                if (parameter is string p
                    && int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedWidth)
                    && parsedWidth > 0)
                {
                    decodeWidth = parsedWidth;
                }

                long lastWriteTicks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                string cacheKey = $"{path}|{decodeWidth}|{lastWriteTicks}";

                lock (ImageCacheLock)
                {
                    if (ImageCache.TryGetValue(cacheKey, out var cached))
                    {
                        TouchImageCacheKey(cacheKey);
                        return cached;
                    }
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;

                // Background images should keep full resolution. Button/icon images can still opt into downscaling.
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }

                bitmap.EndInit();
                bitmap.Freeze();

                lock (ImageCacheLock)
                {
                    AddImageToCache(cacheKey, bitmap);
                }

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

    private static void AddImageToCache(string cacheKey, ImageSource image)
    {
        if (ImageCache.ContainsKey(cacheKey))
        {
            ImageCache[cacheKey] = image;
            TouchImageCacheKey(cacheKey);
            return;
        }

        ImageCache[cacheKey] = image;
        var node = ImageCacheOrder.AddLast(cacheKey);
        ImageCacheNodes[cacheKey] = node;

        while (ImageCache.Count > MaxImageCacheEntries)
            EvictOldestImageCacheEntry();
    }

    private static void TouchImageCacheKey(string cacheKey)
    {
        if (!ImageCacheNodes.TryGetValue(cacheKey, out var node))
            return;

        ImageCacheOrder.Remove(node);
        ImageCacheOrder.AddLast(node);
    }

    private static void EvictOldestImageCacheEntry()
    {
        var oldest = ImageCacheOrder.First;
        if (oldest == null)
            return;

        string key = oldest.Value;
        ImageCacheOrder.RemoveFirst();
        ImageCacheNodes.Remove(key);
        ImageCache.Remove(key);
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

    private static readonly Dictionary<StreamDecky.Models.ButtonShape, Geometry> CachedGeometries = BuildCachedGeometries();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StreamDecky.Models.ButtonShape shape 
            && shape != StreamDecky.Models.ButtonShape.None
            && CachedGeometries.TryGetValue(shape, out var geometry))
        {
            return geometry;
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return StreamDecky.Models.ButtonShape.None;
    }

    private static Dictionary<StreamDecky.Models.ButtonShape, Geometry> BuildCachedGeometries()
    {
        var cache = new Dictionary<StreamDecky.Models.ButtonShape, Geometry>();
        foreach (var pair in ShapePaths)
        {
            try
            {
                var geometry = Geometry.Parse(pair.Value);
                geometry.Freeze();
                cache[pair.Key] = geometry;
            }
            catch
            {
                // Ignore invalid geometry definitions and continue with remaining shapes.
            }
        }

        return cache;
    }
}

public class CellImageBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 4
            && values[0] is string path && !string.IsNullOrEmpty(path)
            && values[1] is int index
            && values[2] is int columns && columns > 0
            && values[3] is int rows && rows > 0)
        {
            var bitmap = OverlayImageCache.TryGet(path);
            if (bitmap == null)
            {
                _ = OverlayImageCache.EnsureLoadedAsync(path);
                return Brushes.Transparent;
            }

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
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class AdaptiveButtonSizeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 6)
            return 80.0;

        double requestedSize = GetDouble(values[0], 80);
        double spacing = Math.Max(0, GetDouble(values[1], 0));
        int columns = Math.Max(1, GetInt(values[2], 1));
        int rows = Math.Max(1, GetInt(values[3], 1));
        double availableWidth = GetDouble(values[4], 0);
        double availableHeight = GetDouble(values[5], 0);

        double widthPadding = 0;
        double heightPadding = 0;
        double minSize = 20;

        if (parameter is string p && !string.IsNullOrWhiteSpace(p))
        {
            var parts = p.Split(',');
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var wPad))
                widthPadding = Math.Max(0, wPad);
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hPad))
                heightPadding = Math.Max(0, hPad);
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMin))
                minSize = Math.Max(8, parsedMin);
        }

        availableWidth = Math.Max(0, availableWidth - widthPadding);
        availableHeight = Math.Max(0, availableHeight - heightPadding);

        if (availableWidth <= 0 || availableHeight <= 0)
            return requestedSize;

        double fitByWidth = (availableWidth / columns) - spacing;
        double fitByHeight = (availableHeight / rows) - spacing;
        double fitted = Math.Min(fitByWidth, fitByHeight);

        if (double.IsNaN(fitted) || double.IsInfinity(fitted) || fitted <= 0)
            return minSize;

        return Math.Max(minSize, Math.Min(requestedSize, fitted));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static double GetDouble(object value, double fallback)
    {
        if (value is double d)
            return d;

        if (value is float f)
            return f;

        if (value is int i)
            return i;

        return fallback;
    }

    private static int GetInt(object value, int fallback)
    {
        if (value is int i)
            return i;

        if (value is double d)
            return (int)d;

        return fallback;
    }
}

public class CellClusterCornerRadiusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4
            || values[0] is not int index
            || values[1] is not int columns || columns <= 0
            || values[2] is not int rows || rows <= 0
            || values[3] is not IList items)
        {
            return new CornerRadius(0);
        }

        if (!IsConfigured(items, index))
            return new CornerRadius(0);

        double radius = 14;
        if (parameter is string p
            && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius))
        {
            radius = Math.Max(0, parsedRadius);
        }

        int col = index % columns;
        int row = index / columns;

        bool hasUp = row > 0 && IsConfigured(items, index - columns);
        bool hasDown = row < rows - 1 && IsConfigured(items, index + columns);
        bool hasLeft = col > 0 && IsConfigured(items, index - 1);
        bool hasRight = col < columns - 1 && IsConfigured(items, index + 1);

        double topLeft = !hasUp && !hasLeft ? radius : 0;
        double topRight = !hasUp && !hasRight ? radius : 0;
        double bottomRight = !hasDown && !hasRight ? radius : 0;
        double bottomLeft = !hasDown && !hasLeft ? radius : 0;

        return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static bool IsConfigured(IList items, int index)
    {
        if (index < 0 || index >= items.Count)
            return false;

        var item = items[index];

        if (item is ButtonViewModel vm)
            return vm.IsConfigured;

        var prop = item?.GetType().GetProperty("IsConfigured");
        return prop?.PropertyType == typeof(bool)
            && prop.GetValue(item) is bool configured
            && configured;
    }
}
