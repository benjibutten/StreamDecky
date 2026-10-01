using System.IO;
using System.Text.Json;
using StreamDecky.Helpers;

namespace StreamDecky.SpeedReader;

/// <summary>
/// Loads and saves <see cref="SpeedReaderSettings"/> as
/// %LOCALAPPDATA%\StreamDecky\speed-reader.json. An unreadable file falls back to
/// defaults, which is also what a first run looks like.
/// </summary>
public sealed class SpeedReaderSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _folder;
    private readonly string _settingsPath;

    public SpeedReaderSettingsStore(string? appDataFolder = null)
    {
        _folder = string.IsNullOrWhiteSpace(appDataFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDecky")
            : appDataFolder;

        _settingsPath = Path.Combine(_folder, "speed-reader.json");
    }

    public SpeedReaderSettings Load()
    {
        var settings = new SpeedReaderSettings();

        try
        {
            if (File.Exists(_settingsPath))
                settings = JsonSerializer.Deserialize<SpeedReaderSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? settings;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to load speed reader settings '{_settingsPath}'. Falling back to defaults.", ex);
            CorruptFileQuarantine.MoveAside(_settingsPath);
        }

        settings.Normalize();
        return settings;
    }

    public void Save(SpeedReaderSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            string tempPath = Path.Combine(_folder, $"speed-reader.json.{Guid.NewGuid():N}.tmp");
            DurableFile.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Failed to save speed reader settings '{_settingsPath}'.", ex);
        }
    }
}
