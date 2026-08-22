using System.IO;
using System.Text.Json;
using StreamDecky.Helpers;
using StreamDecky.Models;

namespace StreamDecky.Services;

/// <summary>
/// Persists machine-wide settings in %LOCALAPPDATA%\StreamDecky\app-settings.json.
/// Kept out of profiles.json because profiles are exported and shared, and the
/// DeepSeek API key must never leave the machine with them.
/// </summary>
public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _appDataFolder;
    private readonly string _settingsPath;
    private AppSettings? _settings;
    private bool _persistenceBlocked;

    public AppSettingsService(string? appDataFolder = null)
    {
        _appDataFolder = string.IsNullOrWhiteSpace(appDataFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDecky")
            : appDataFolder;

        _settingsPath = Path.Combine(_appDataFolder, "app-settings.json");
    }

    private AppSettings Settings
    {
        get
        {
            if (_settings == null)
            {
                // Assign before migrating: the migration saves, and Save() reads Settings.
                _settings = Load();
                UpgradeUnprotectedApiKeys();
            }

            return _settings;
        }
    }

    private void UpgradeUnprotectedApiKeys()
    {
        UpgradeUnprotectedApiKey(
            "DeepSeek",
            () => _settings!.DeepSeekApiKeyProtected,
            value => _settings!.DeepSeekApiKeyProtected = value);

        UpgradeUnprotectedApiKey(
            "Brave",
            () => _settings!.BraveApiKeyProtected,
            value => _settings!.BraveApiKeyProtected = value);
    }

    /// <summary>
    /// Re-protects a key left behind in a readable form by an older build or by hand
    /// editing. Without this it would stay readable forever, since re-entering the same
    /// key is a no-op and every later settings change would rewrite it as it was.
    /// </summary>
    private void UpgradeUnprotectedApiKey(string keyName, Func<string> read, Action<string> write)
    {
        string stored = read();
        if (DataProtection.IsProtected(stored))
            return;

        string clearKey = DataProtection.Unprotect(stored);
        if (string.IsNullOrEmpty(clearKey))
            return;

        if (!DataProtection.TryProtect(clearKey, out string protectedValue))
        {
            AppDiagnostics.Error(
                $"An unprotected {keyName} API key could not be re-protected and is still readable in app-settings.json.");
            return;
        }

        write(protectedValue);

        if (Save())
            AppDiagnostics.Warning($"An unprotected {keyName} API key was re-protected with DPAPI.");
        else
            write(stored);
    }

    /// <summary>The DeepSeek API key in clear text; empty when unset or unreadable.</summary>
    public string DeepSeekApiKey => DataProtection.Unprotect(Settings.DeepSeekApiKeyProtected);

    /// <summary>
    /// Stores the API key, or reports why it could not be stored. Fails closed: if
    /// Windows cannot protect the value it is not written at all, so the key never
    /// lands on disk in a reversible form.
    /// </summary>
    public bool TrySetDeepSeekApiKey(string? value, out string? error) =>
        TrySetApiKey(
            value,
            () => DeepSeekApiKey,
            () => Settings.DeepSeekApiKeyProtected,
            stored => Settings.DeepSeekApiKeyProtected = stored,
            out error);

    public bool HasDeepSeekApiKey => !string.IsNullOrWhiteSpace(DeepSeekApiKey);

    /// <summary>The Brave Search API key in clear text; empty when unset or unreadable.</summary>
    public string BraveApiKey => DataProtection.Unprotect(Settings.BraveApiKeyProtected);

    public bool TrySetBraveApiKey(string? value, out string? error) =>
        TrySetApiKey(
            value,
            () => BraveApiKey,
            () => Settings.BraveApiKeyProtected,
            stored => Settings.BraveApiKeyProtected = stored,
            out error);

    public bool HasBraveApiKey => !string.IsNullOrWhiteSpace(BraveApiKey);

    /// <summary>
    /// Stores an API key, or reports why it could not be stored. Fails closed: if Windows
    /// cannot protect the value it is not written at all, so the key never lands on disk
    /// in a reversible form.
    /// </summary>
    private bool TrySetApiKey(
        string? value,
        Func<string> readClear,
        Func<string> readStored,
        Action<string> writeStored,
        out string? error)
    {
        error = null;
        string trimmed = value?.Trim() ?? string.Empty;

        // An unchanged key is still worth rewriting when what is on disk is not
        // protected, so re-entering the same key repairs a readable one.
        if (string.Equals(readClear(), trimmed, StringComparison.Ordinal)
            && DataProtection.IsProtected(readStored()))
        {
            return true;
        }

        if (!DataProtection.TryProtect(trimmed, out string protectedValue))
        {
            error = "Windows could not protect the API key, so it was not saved. See the log for details.";
            return false;
        }

        string previous = readStored();
        writeStored(protectedValue);

        if (Save())
            return true;

        // Roll back so the getter cannot report a key that never reached the disk.
        writeStored(previous);
        error = _persistenceBlocked
            ? "The settings file was written by a newer version of StreamDecky, so the API key was not saved."
            : "The API key could not be written to app-settings.json. See the log for details.";
        return false;
    }

    public string DeepSeekModel
    {
        get => Settings.DeepSeekModel;
        set
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultDeepSeekModel : value.Trim();
            if (string.Equals(Settings.DeepSeekModel, trimmed, StringComparison.Ordinal))
                return;

            Settings.DeepSeekModel = trimmed;
            Save();
        }
    }

    public string SpellCheckPrompt
    {
        get => Settings.SpellCheckPrompt;
        set
        {
            string prompt = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultSpellCheckPrompt : value;
            if (string.Equals(Settings.SpellCheckPrompt, prompt, StringComparison.Ordinal))
                return;

            Settings.SpellCheckPrompt = prompt;
            Save();
        }
    }

    /// <summary>
    /// How much reasoning DeepSeek should spend before answering. Defaults to
    /// <see cref="AppSettings.ThinkingDisabled"/>, which is both the fastest and the
    /// cheapest choice for respelling a line of chat.
    /// </summary>
    public string SpellCheckThinking
    {
        get => Settings.SpellCheckThinking;
        set
        {
            string normalized = AppSettings.NormalizeThinking(value);
            if (string.Equals(Settings.SpellCheckThinking, normalized, StringComparison.Ordinal))
                return;

            Settings.SpellCheckThinking = normalized;
            Save();
        }
    }

    public string QuickAnswerPrompt
    {
        get => Settings.QuickAnswerPrompt;
        set
        {
            string prompt = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultQuickAnswerPrompt : value;
            if (string.Equals(Settings.QuickAnswerPrompt, prompt, StringComparison.Ordinal))
                return;

            Settings.QuickAnswerPrompt = prompt;
            Save();
        }
    }

    /// <summary>
    /// Kept apart from <see cref="SpellCheckThinking"/>: an answer the user may read out
    /// on stream is worth a little reasoning, a respelling never is.
    /// </summary>
    public string QuickAnswerThinking
    {
        get => Settings.QuickAnswerThinking;
        set
        {
            string normalized = AppSettings.NormalizeThinking(value, AppSettings.DefaultQuickAnswerThinking);
            if (string.Equals(Settings.QuickAnswerThinking, normalized, StringComparison.Ordinal))
                return;

            Settings.QuickAnswerThinking = normalized;
            Save();
        }
    }

    public string QuickAnswerSearchMode
    {
        get => Settings.QuickAnswerSearchMode;
        set
        {
            string normalized = AppSettings.NormalizeSearchMode(value);
            if (string.Equals(Settings.QuickAnswerSearchMode, normalized, StringComparison.Ordinal))
                return;

            Settings.QuickAnswerSearchMode = normalized;
            Save();
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = new AppSettings();
            defaults.Initialize();
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                _persistenceBlocked = true;
                AppDiagnostics.Warning(
                    $"App settings schema version {settings.SchemaVersion} is newer than supported version {AppSettings.CurrentSchemaVersion}. "
                    + "The settings will load, but saving is blocked to avoid data loss.");
            }

            settings.Initialize();
            return settings;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to load app settings '{_settingsPath}'. Falling back to defaults.", ex);
            var fallback = new AppSettings();
            fallback.Initialize();
            return fallback;
        }
    }

    /// <summary>
    /// Writes the settings file. Returns false when nothing reached the disk, so callers
    /// that promised the user something was saved can say otherwise.
    /// </summary>
    private bool Save()
    {
        if (_persistenceBlocked)
            return false;

        try
        {
            Directory.CreateDirectory(_appDataFolder);
            Settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            string tempPath = Path.Combine(_appDataFolder, $"app-settings.json.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, json);

            if (File.Exists(_settingsPath))
                File.Move(tempPath, _settingsPath, overwrite: true);
            else
                File.Move(tempPath, _settingsPath);

            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Failed to save app settings '{_settingsPath}'.", ex);
            return false;
        }
    }
}
