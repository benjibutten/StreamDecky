using System.IO;

namespace StreamDecky.Helpers;

public static class DurableFile
{
    /// <summary>
    /// Like File.WriteAllText, but forces the data to disk before returning. A rename
    /// after a plain write is journaled by NTFS while the file contents may still sit
    /// in the cache, so a power cut can leave the renamed file zero-filled.
    /// </summary>
    public static void WriteAllText(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            writer.Write(content);
        }

        stream.Flush(flushToDisk: true);
    }

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
        }

        stream.Flush(flushToDisk: true);
    }
}
