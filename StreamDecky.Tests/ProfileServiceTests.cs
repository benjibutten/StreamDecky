using System.Text.Json;
using StreamDecky.Models;
using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public void LoadStore_WhenNoFilesExist_ReturnsDefaultStore()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new ProfileService(tempDirectory.Path);

        DeckProfileStore store = service.LoadStore();

        DeckProfile activeProfile = store.GetActiveProfile();
        Assert.Single(store.Profiles);
        Assert.Equal("Standard", activeProfile.Name);
        Assert.Equal(activeProfile.Id, store.ActiveProfileId);
    }

    [Fact]
    public void LoadStore_WhenProfilesFileIsInvalid_FallsBackToLegacyAndRefreshesBackup()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilesPath = System.IO.Path.Combine(tempDirectory.Path, "profiles.json");
        string legacyPath = System.IO.Path.Combine(tempDirectory.Path, "profile.json");
        string backupPath = System.IO.Path.Combine(tempDirectory.Path, "profiles.backup.json");

        System.IO.File.WriteAllText(profilesPath, "{ definitely-not-json }");

        var legacyProfile = new DeckProfile { Name = "Legacy" };
        var legacyService = new ProfileService(tempDirectory.Path);
        string legacyJson = legacyService.Serialize(legacyProfile);
        System.IO.File.WriteAllText(legacyPath, legacyJson);

        var service = new ProfileService(tempDirectory.Path);
        DeckProfileStore store = service.LoadStore();

        Assert.Equal("Legacy", store.GetActiveProfile().Name);
        Assert.True(System.IO.File.Exists(backupPath));
        Assert.Equal(legacyJson, System.IO.File.ReadAllText(backupPath));
    }

    [Fact]
    public void SaveStore_WhenProfilesChange_CreatesBackupAndCleansTempFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilesPath = System.IO.Path.Combine(tempDirectory.Path, "profiles.json");
        string backupPath = System.IO.Path.Combine(tempDirectory.Path, "profiles.backup.json");
        var service = new ProfileService(tempDirectory.Path);

        var originalStore = new DeckProfileStore
        {
            Profiles = new List<DeckProfile>
            {
                new() { Name = "Original" }
            }
        };
        originalStore.Initialize();

        string originalJson = service.SerializeStore(originalStore);
        System.IO.File.WriteAllText(profilesPath, originalJson);

        var updatedStore = new DeckProfileStore
        {
            Profiles = new List<DeckProfile>
            {
                new() { Name = "Updated" }
            }
        };
        updatedStore.Initialize();

        service.SaveStore(updatedStore);

        string expectedUpdatedJson = service.SerializeStore(updatedStore);
        Assert.Equal(expectedUpdatedJson, System.IO.File.ReadAllText(profilesPath));
        Assert.Equal(originalJson, System.IO.File.ReadAllText(backupPath));
        Assert.Empty(System.IO.Directory.GetFiles(tempDirectory.Path, "*.tmp"));
        Assert.Empty(System.IO.Directory.GetFiles(tempDirectory.Path, "*.bak"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StreamDecky.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Path))
                System.IO.Directory.Delete(Path, recursive: true);
        }
    }
}