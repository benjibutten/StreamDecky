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
        Assert.Equal(ProfileSchemaVersion.Current, store.SchemaVersion);
        Assert.Equal(ProfileSchemaVersion.Current, activeProfile.SchemaVersion);
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

        [Fact]
        public void LoadStore_WhenProfilesFileIsUnversioned_MigratesLegacyLayoutAndStickyNotes()
        {
                using var tempDirectory = new TemporaryDirectory();
                string profilesPath = System.IO.Path.Combine(tempDirectory.Path, "profiles.json");

                System.IO.File.WriteAllText(
                        profilesPath,
                        """
                        {
                            "ActiveProfileId": "legacy-profile",
                            "Profiles": [
                                {
                                    "Id": "legacy-profile",
                                    "Name": "Legacy",
                                    "LayoutRows": 0,
                                    "LayoutColumns": 0,
                                    "Pages": [
                                        {
                                            "Id": "page-1",
                                            "Name": "Page 1",
                                            "Rows": 4,
                                            "Columns": 6,
                                            "Buttons": [],
                                            "StickyNotes": [
                                                {
                                                    "Id": "note-1",
                                                    "Title": "Sticky note",
                                                    "Text": "Legacy note"
                                                }
                                            ]
                                        }
                                    ]
                                }
                            ]
                        }
                        """);

                var service = new ProfileService(tempDirectory.Path);

                DeckProfileStore store = service.LoadStore();
                DeckProfile profile = store.GetActiveProfile();

                Assert.Equal(ProfileSchemaVersion.Current, store.SchemaVersion);
                Assert.Equal(ProfileSchemaVersion.Current, profile.SchemaVersion);
                Assert.Equal(4, profile.LayoutRows);
                Assert.Equal(6, profile.LayoutColumns);
                Assert.Single(profile.NotePages);
                Assert.Single(profile.NotePages[0].StickyNotes);
                Assert.Equal("Legacy note", profile.NotePages[0].StickyNotes[0].Text);
                Assert.Empty(profile.Pages[0].StickyNotes);
        }

        [Fact]
        public void SerializeProfileJson_WritesCurrentSchemaVersion()
        {
                var profile = new DeckProfile { Name = "Exported" };

                string json = ProfileService.SerializeProfileJson(profile);

                Assert.Contains($"\"SchemaVersion\": {ProfileSchemaVersion.Current}", json, StringComparison.Ordinal);
        }

        [Fact]
        public void SerializeAndDeserializeProfileJson_RoundTripsNotesQuickTextAndLayout()
        {
            var category = new QuickTextCategory { Id = "cat-1", Name = "Macros" };
            var profile = new DeckProfile
            {
                Name = "Roundtrip",
                LayoutRows = 4,
                LayoutColumns = 6,
                Pages = new List<DeckPage>
                {
                    new() { Name = "Page 1", Rows = 4, Columns = 6 }
                },
                NotePages = new List<NotePage>
                {
                    new()
                    {
                        Name = "Notes 1",
                        StickyNotes = new List<StickyNote>
                        {
                            new() { Text = "Remember this" }
                        }
                    }
                },
                QuickTextCategories = new List<QuickTextCategory> { category },
                ActiveQuickTextCategoryId = category.Id,
                QuickTextItems = new List<QuickTextItem>
                {
                    new() { CategoryId = category.Id, Text = "Clip item" }
                },
                QuickTextActionSteps = new List<ActionStep>
                {
                    new() { Type = ActionStepType.TextInput, Text = "{{itemText}}" }
                }
            };

            string json = ProfileService.SerializeProfileJson(profile);
            DeckProfile imported = ProfileService.DeserializeProfileJson(json);

            Assert.Equal(ProfileSchemaVersion.Current, imported.SchemaVersion);
            Assert.Equal(4, imported.LayoutRows);
            Assert.Equal(6, imported.LayoutColumns);
            Assert.Single(imported.NotePages);
            Assert.Equal("Remember this", imported.NotePages[0].StickyNotes[0].Text);
            Assert.Single(imported.QuickTextCategories);
            Assert.Equal(category.Id, imported.ActiveQuickTextCategoryId);
            Assert.Single(imported.QuickTextItems);
            Assert.Equal("Clip item", imported.QuickTextItems[0].Text);
            Assert.Single(imported.QuickTextActionSteps);
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