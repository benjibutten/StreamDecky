using System.IO;
using System.Text;

namespace StreamDecky.Helpers;

public static class AppDiagnostics
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamDecky",
        "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "streamdecky.log");

    public static void Info(string message)
    {
        Write("INFO", message, exception: null);
    }

    public static void Warning(string message, Exception? exception = null)
    {
        Write("WARN", message, exception);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var builder = new StringBuilder()
                .Append(DateTimeOffset.UtcNow.ToString("O"))
                .Append(' ')
                .Append(level)
                .Append(' ')
                .Append(message);

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            builder.AppendLine();

            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, builder.ToString());
            }
        }
        catch
        {
            // Diagnostics must never interfere with app behavior.
        }
    }
}