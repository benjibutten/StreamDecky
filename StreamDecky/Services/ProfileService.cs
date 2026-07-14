using System.IO;
using System.Text.Json;
using StreamDecky.Helpers;
using StreamDecky.Models;

namespace StreamDecky.Services;

public class ProfileService
{
    private readonly string _appDataFolder;
    private readonly string _legacyProfilePath;
    private readonly string _profilesPath;
    private readonly string _profilesBackupPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public ProfileService(string? appDataFolder = null)
    {
        _appDataFolder = string.IsNullOrWhiteSpace(appDataFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDecky")
            : appDataFolder;

        _legacyProfilePath = Path.Combine(_appDataFolder, "profile.json");
        _profilesPath = Path.Combine(_appDataFolder, "profiles.json");
        _profilesBackupPath = Path.Combine(_appDataFolder, "profiles.backup.json");
    }

    public static DeckProfile DeserializeProfileJson(string json)
    {
        var profile = JsonSerializer.Deserialize<DeckProfile>(json, JsonOptions) ?? new DeckProfile();
        ProfileSchemaMigrator.MigrateProfile(profile);
        return profile;
    }

    public static DeckProfileStore DeserializeStoreJson(string json)
    {
        var store = JsonSerializer.Deserialize<DeckProfileStore>(json, JsonOptions) ?? new DeckProfileStore();
        ProfileSchemaMigrator.MigrateStore(store);
        return store;
    }

    public static string SerializeProfileJson(DeckProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ProfileSchemaMigrator.PrepareProfileForPersistence(profile);
        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    public static string SerializeStoreJson(DeckProfileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        ProfileSchemaMigrator.PrepareStoreForPersistence(store);
        return JsonSerializer.Serialize(store, JsonOptions);
    }

    public DeckProfile Load()
    {
        return LoadLegacyProfile();
    }

    public DeckProfileStore LoadStore()
    {
        if (File.Exists(_profilesPath))
        {
            try
            {
                var json = File.ReadAllText(_profilesPath);
                int sourceSchemaVersion = ReadSchemaVersion(json);
                var store = DeserializeStoreJson(json);
                PreservePreMigrationBackup(_profilesPath, json, sourceSchemaVersion);
                RefreshStartupBackupIfChanged(_profilesPath);
                return store;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Warning($"Failed to load '{_profilesPath}'. Falling back to legacy profile store.", ex);
                var fallbackStore = CreateStoreFromLegacyProfile();
                if (File.Exists(_legacyProfilePath))
                    RefreshStartupBackupIfChanged(_legacyProfilePath);
                return fallbackStore;
            }
        }

        var migratedStore = CreateStoreFromLegacyProfile();
        if (File.Exists(_legacyProfilePath))
            RefreshStartupBackupIfChanged(_legacyProfilePath);
        return migratedStore;
    }

    private void RefreshStartupBackupIfChanged(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return;

            Directory.CreateDirectory(_appDataFolder);

            var sourceJson = File.ReadAllText(sourcePath);
            if (File.Exists(_profilesBackupPath))
            {
                var backupJson = File.ReadAllText(_profilesBackupPath);
                if (string.Equals(sourceJson, backupJson, StringComparison.Ordinal))
                    return;
            }

            WriteTextAtomically(_profilesBackupPath, sourceJson);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to refresh startup backup from '{sourcePath}'.", ex);
        }
    }

    private void BackupCurrentProfilesIfChanged(string nextJson)
    {
        try
        {
            if (!File.Exists(_profilesPath))
                return;

            var currentJson = File.ReadAllText(_profilesPath);
            if (string.Equals(currentJson, nextJson, StringComparison.Ordinal))
                return;

            Directory.CreateDirectory(_appDataFolder);
            WriteTextAtomically(_profilesBackupPath, currentJson);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to create profile backup '{_profilesBackupPath}'.", ex);
        }
    }

    private async Task BackupCurrentProfilesIfChangedAsync(string nextJson, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_profilesPath))
                return;

            var currentJson = await File.ReadAllTextAsync(_profilesPath, cancellationToken);
            if (string.Equals(currentJson, nextJson, StringComparison.Ordinal))
                return;

            Directory.CreateDirectory(_appDataFolder);
            await WriteTextAtomicallyAsync(_profilesBackupPath, currentJson, cancellationToken);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to create async profile backup '{_profilesBackupPath}'.", ex);
        }
    }

    private DeckProfileStore CreateStoreFromLegacyProfile()
    {
        var profile = LoadLegacyProfile();
        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Standard" : profile.Name;

        var store = new DeckProfileStore
        {
            ActiveProfileId = profile.Id,
            Profiles = new List<DeckProfile> { profile }
        };

        ProfileSchemaMigrator.MigrateStore(store);
        return store;
    }

    private DeckProfile LoadLegacyProfile()
    {
        if (!File.Exists(_legacyProfilePath))
        {
            return CreateDefaultProfile();
        }

        try
        {
            var json = File.ReadAllText(_legacyProfilePath);
            int sourceSchemaVersion = ReadSchemaVersion(json);
            DeckProfile profile = DeserializeProfileJson(json);
            PreservePreMigrationBackup(_legacyProfilePath, json, sourceSchemaVersion);
            return profile;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to load legacy profile '{_legacyProfilePath}'. Falling back to default profile.", ex);
            return CreateDefaultProfile();
        }
    }

    private static DeckProfile CreateDefaultProfile()
    {
        var profile = new DeckProfile
        {
            Name = "Standard"
        };
        ProfileSchemaMigrator.PrepareProfileForPersistence(profile);
        return profile;
    }

    private static int ReadSchemaVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty(nameof(DeckProfile.SchemaVersion), out JsonElement versionElement)
            && versionElement.TryGetInt32(out int version))
        {
            return version <= ProfileSchemaVersion.Unspecified ? ProfileSchemaVersion.Baseline : version;
        }

        return ProfileSchemaVersion.Baseline;
    }

    private void PreservePreMigrationBackup(string sourcePath, string sourceJson, int sourceSchemaVersion)
    {
        if (sourceSchemaVersion >= ProfileSchemaVersion.Current)
            return;

        try
        {
            Directory.CreateDirectory(_appDataFolder);
            string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
            string backupPath = Path.Combine(
                _appDataFolder,
                $"{sourceName}.pre-migration-v{sourceSchemaVersion}.json");
            if (!File.Exists(backupPath))
                WriteTextAtomically(backupPath, sourceJson);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning(
                $"Failed to preserve the pre-migration backup for '{sourcePath}'.",
                ex);
        }
    }

    public void Save(DeckProfile profile)
    {
        try
        {
            Directory.CreateDirectory(_appDataFolder);
            var json = Serialize(profile);
            WriteTextAtomically(_legacyProfilePath, json);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Failed to save legacy profile '{_legacyProfilePath}'.", ex);
            throw;
        }
    }

    public void SaveStore(DeckProfileStore store)
    {
        try
        {
            store.Initialize();
            Directory.CreateDirectory(_appDataFolder);
            var json = SerializeStore(store);
            BackupCurrentProfilesIfChanged(json);
            WriteTextAtomically(_profilesPath, json);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Failed to save profile store '{_profilesPath}'.", ex);
            throw;
        }
    }

    public string Serialize(DeckProfile profile)
    {
        return SerializeProfileJson(profile);
    }

    public string SerializeStore(DeckProfileStore store)
    {
        return SerializeStoreJson(store);
    }

    public async Task SaveSerializedAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_appDataFolder);
            await WriteTextAtomicallyAsync(_legacyProfilePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Failed to save serialized legacy profile '{_legacyProfilePath}'.", ex);
            throw;
        }
    }

    public async Task SaveStoreSerializedAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_appDataFolder);
            await BackupCurrentProfilesIfChangedAsync(json, cancellationToken);
            await WriteTextAtomicallyAsync(_profilesPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Failed to save serialized profile store '{_profilesPath}'.", ex);
            throw;
        }
    }

    private void WriteTextAtomically(string destinationPath, string content)
    {
        string tempPath = Path.Combine(_appDataFolder, $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content);
        ReplaceFile(tempPath, destinationPath);
    }

    private async Task WriteTextAtomicallyAsync(string destinationPath, string content, CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(_appDataFolder, $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        ReplaceFile(tempPath, destinationPath);
    }

    private static void ReplaceFile(string tempPath, string destinationPath)
    {
        string? backupPath = null;

        try
        {
            if (File.Exists(destinationPath))
            {
                backupPath = tempPath + ".bak";
                File.Replace(tempPath, destinationPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);

            if (!string.IsNullOrWhiteSpace(backupPath))
                TryDeleteFile(backupPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary cleanup failures should not mask the original result.
        }
    }
}
