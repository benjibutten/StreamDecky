using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace StreamDecky.Helpers;

public static class OverlayImageCache
{
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> LoadingTasks = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? TryGet(string? path)
    {
        string? normalized = NormalizePath(path);
        if (normalized == null)
            return null;

        return Cache.TryGetValue(normalized, out var bitmap) ? bitmap : null;
    }

    public static async Task<bool> EnsureLoadedAsync(string? path)
    {
        string? normalized = NormalizePath(path);
        if (normalized == null)
            return false;

        if (Cache.ContainsKey(normalized))
            return true;

        var lazyTask = LoadingTasks.GetOrAdd(normalized,
            static p => new Lazy<Task<BitmapSource?>>(() => Task.Run(() => LoadBitmap(p))));

        BitmapSource? bitmap;
        try
        {
            bitmap = await lazyTask.Value.ConfigureAwait(false);
        }
        finally
        {
            LoadingTasks.TryRemove(normalized, out _);
        }

        if (bitmap == null)
            return false;

        Cache[normalized] = bitmap;
        return true;
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
