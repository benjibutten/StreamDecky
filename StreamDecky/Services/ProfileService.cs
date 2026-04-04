using System.IO;
using System.Text.Json;
using StreamDecky.Models;

namespace StreamDecky.Services;

public class ProfileService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamDecky");

    private static readonly string LegacyProfilePath = Path.Combine(AppDataFolder, "profile.json");
    private static readonly string ProfilesPath = Path.Combine(AppDataFolder, "profiles.json");
    private static readonly string ProfilesBackupPath = Path.Combine(AppDataFolder, "profiles.backup.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public DeckProfile Load()
    {
        return LoadLegacyProfile();
    }

    public DeckProfileStore LoadStore()
    {
        if (File.Exists(ProfilesPath))
        {
            try
            {
                var json = File.ReadAllText(ProfilesPath);
                var store = JsonSerializer.Deserialize<DeckProfileStore>(json, JsonOptions) ?? new DeckProfileStore();
                store.Initialize();
                RefreshStartupBackupIfChanged(ProfilesPath);
                return store;
            }
            catch
            {
                var fallbackStore = CreateStoreFromLegacyProfile();
                fallbackStore.Initialize();
                if (File.Exists(LegacyProfilePath))
                    RefreshStartupBackupIfChanged(LegacyProfilePath);
                return fallbackStore;
            }
        }

        var migratedStore = CreateStoreFromLegacyProfile();
        migratedStore.Initialize();
        if (File.Exists(LegacyProfilePath))
            RefreshStartupBackupIfChanged(LegacyProfilePath);
        return migratedStore;
    }

    private static void RefreshStartupBackupIfChanged(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return;

            Directory.CreateDirectory(AppDataFolder);

            var sourceJson = File.ReadAllText(sourcePath);
            if (File.Exists(ProfilesBackupPath))
            {
                var backupJson = File.ReadAllText(ProfilesBackupPath);
                if (string.Equals(sourceJson, backupJson, StringComparison.Ordinal))
                    return;
            }

            File.WriteAllText(ProfilesBackupPath, sourceJson);
        }
        catch
        {
            // Backup must never block app startup.
        }
    }

    private static void BackupCurrentProfilesIfChanged(string nextJson)
    {
        try
        {
            if (!File.Exists(ProfilesPath))
                return;

            var currentJson = File.ReadAllText(ProfilesPath);
            if (string.Equals(currentJson, nextJson, StringComparison.Ordinal))
                return;

            Directory.CreateDirectory(AppDataFolder);
            File.WriteAllText(ProfilesBackupPath, currentJson);
        }
        catch
        {
            // Ignore backup write failures; primary save still proceeds.
        }
    }

    private static async Task BackupCurrentProfilesIfChangedAsync(string nextJson, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(ProfilesPath))
                return;

            var currentJson = await File.ReadAllTextAsync(ProfilesPath, cancellationToken);
            if (string.Equals(currentJson, nextJson, StringComparison.Ordinal))
                return;

            Directory.CreateDirectory(AppDataFolder);
            await File.WriteAllTextAsync(ProfilesBackupPath, currentJson, cancellationToken);
        }
        catch
        {
            // Ignore backup write failures; primary save still proceeds.
        }
    }

    private DeckProfileStore CreateStoreFromLegacyProfile()
    {
        var profile = LoadLegacyProfile();
        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Standard" : profile.Name;

        return new DeckProfileStore
        {
            ActiveProfileId = profile.Id,
            Profiles = new List<DeckProfile> { profile }
        };
    }

    private DeckProfile LoadLegacyProfile()
    {
        if (!File.Exists(LegacyProfilePath))
        {
            return CreateDefaultProfile();
        }

        try
        {
            var json = File.ReadAllText(LegacyProfilePath);
            var profile = JsonSerializer.Deserialize<DeckProfile>(json, JsonOptions) ?? new DeckProfile();
            profile.Initialize();
            return profile;
        }
        catch
        {
            return CreateDefaultProfile();
        }
    }

    private static DeckProfile CreateDefaultProfile()
    {
        var profile = new DeckProfile
        {
            Name = "Standard"
        };
        profile.Initialize();
        return profile;
    }

    public void Save(DeckProfile profile)
    {
        Directory.CreateDirectory(AppDataFolder);
        var json = Serialize(profile);
        File.WriteAllText(LegacyProfilePath, json);
    }

    public void SaveStore(DeckProfileStore store)
    {
        store.Initialize();
        Directory.CreateDirectory(AppDataFolder);
        var json = SerializeStore(store);
        BackupCurrentProfilesIfChanged(json);
        File.WriteAllText(ProfilesPath, json);
    }

    public string Serialize(DeckProfile profile)
    {
        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    public string SerializeStore(DeckProfileStore store)
    {
        store.Initialize();
        return JsonSerializer.Serialize(store, JsonOptions);
    }

    public async Task SaveSerializedAsync(string json, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDataFolder);
        await File.WriteAllTextAsync(LegacyProfilePath, json, cancellationToken);
    }

    public async Task SaveStoreSerializedAsync(string json, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDataFolder);
        await BackupCurrentProfilesIfChangedAsync(json, cancellationToken);
        await File.WriteAllTextAsync(ProfilesPath, json, cancellationToken);
    }
}
