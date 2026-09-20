using System.IO;

namespace StreamDecky.Helpers;

public static class CorruptFileQuarantine
{
    /// <summary>
    /// Moves a store file the app could not read out of the way so the next save does not
    /// overwrite it. The user's data stays on disk under a timestamped name for manual recovery.
    /// </summary>
    public static void MoveAside(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string quarantinePath = Path.Combine(directory, $"{name}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}{extension}");

            File.Move(path, quarantinePath, overwrite: true);
            AppDiagnostics.Warning($"Moved unreadable file '{path}' to '{quarantinePath}'.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to move unreadable file '{path}' aside.", ex);
        }
    }
}
