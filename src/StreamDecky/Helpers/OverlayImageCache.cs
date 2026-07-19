using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace StreamDecky.Helpers;

public static class OverlayImageCache
{
    private const int MaxCacheEntries = 8;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LinkedListNode<string>> CacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> CacheOrder = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> LoadingTasks = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? TryGet(string? path)
    {
        string? normalized = NormalizePath(path);
        if (normalized == null)
            return null;

        lock (CacheLock)
        {
            if (!Cache.TryGetValue(normalized, out var bitmap))
                return null;

            TouchKey(normalized);
            return bitmap;
        }
    }

    public static async Task<bool> EnsureLoadedAsync(string? path)
    {
        string? normalized = NormalizePath(path);
        if (normalized == null)
            return false;

        lock (CacheLock)
        {
            if (Cache.ContainsKey(normalized))
            {
                TouchKey(normalized);
                return true;
            }
        }

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

        lock (CacheLock)
        {
            AddOrUpdateCache(normalized, bitmap);
        }

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

    private static void AddOrUpdateCache(string key, BitmapSource bitmap)
    {
        if (Cache.ContainsKey(key))
        {
            Cache[key] = bitmap;
            TouchKey(key);
            return;
        }

        Cache[key] = bitmap;
        var node = CacheOrder.AddLast(key);
        CacheNodes[key] = node;

        while (Cache.Count > MaxCacheEntries)
            EvictOldest();
    }

    private static void TouchKey(string key)
    {
        if (!CacheNodes.TryGetValue(key, out var node))
            return;

        CacheOrder.Remove(node);
        CacheOrder.AddLast(node);
    }

    private static void EvictOldest()
    {
        var oldest = CacheOrder.First;
        if (oldest == null)
            return;

        string key = oldest.Value;
        CacheOrder.RemoveFirst();
        CacheNodes.Remove(key);
        Cache.Remove(key);
    }
}
