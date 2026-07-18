using StreamDecky.Helpers;
using StreamDecky.Models;

namespace StreamDecky.Services;

public static class ProfileSchemaMigrator
{
    public static void MigrateStore(DeckProfileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        store.Profiles ??= new List<DeckProfile>();
        if (store.SchemaVersion > ProfileSchemaVersion.Current)
        {
            AppDiagnostics.Warning(
                $"Profile store schema version {store.SchemaVersion} is newer than supported version {ProfileSchemaVersion.Current}. "
                + "The store will load without migration, and saving is blocked to avoid data loss.");
            store.Initialize();
            return;
        }

        int version = NormalizeVersion(store.SchemaVersion);
        while (version < ProfileSchemaVersion.Current)
        {
            switch (version)
            {
                case ProfileSchemaVersion.Baseline:
                    ApplyStoreV1ToV2(store);
                    version = ProfileSchemaVersion.StickyNotesAndLayoutMigration;
                    break;
                case ProfileSchemaVersion.StickyNotesAndLayoutMigration:
                    ApplyStoreV2ToV3(store);
                    version = ProfileSchemaVersion.QuickTextCollectionsAndTags;
                    break;
                case ProfileSchemaVersion.QuickTextCollectionsAndTags:
                    ApplyStoreV3ToV4(store);
                    version = ProfileSchemaVersion.GlobalQuickTextTags;
                    break;
                case ProfileSchemaVersion.GlobalQuickTextTags:
                    ApplyStoreV4ToV5(store);
                    version = ProfileSchemaVersion.FormsTemplates;
                    break;
                default:
                    throw CreateMissingMigrationException("profile store", version);
            }
        }

        foreach (var profile in store.Profiles)
            MigrateProfile(profile);

        store.SchemaVersion = ProfileSchemaVersion.Current;
        store.Initialize();
    }

    public static void MigrateProfile(DeckProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SchemaVersion > ProfileSchemaVersion.Current)
        {
            string profileLabel = GetProfileLabel(profile);
            AppDiagnostics.Warning(
                $"Profile {profileLabel} uses schema version {profile.SchemaVersion}, which is newer than supported version {ProfileSchemaVersion.Current}. "
                + "The profile will load without migration, and saving is blocked to avoid data loss.");
            profile.Initialize();
            return;
        }

        int version = NormalizeVersion(profile.SchemaVersion);
        while (version < ProfileSchemaVersion.Current)
        {
            switch (version)
            {
                case ProfileSchemaVersion.Baseline:
                    ApplyProfileV1ToV2(profile);
                    version = ProfileSchemaVersion.StickyNotesAndLayoutMigration;
                    break;
                case ProfileSchemaVersion.StickyNotesAndLayoutMigration:
                    ApplyProfileV2ToV3(profile);
                    version = ProfileSchemaVersion.QuickTextCollectionsAndTags;
                    break;
                case ProfileSchemaVersion.QuickTextCollectionsAndTags:
                    ApplyProfileV3ToV4(profile);
                    version = ProfileSchemaVersion.GlobalQuickTextTags;
                    break;
                case ProfileSchemaVersion.GlobalQuickTextTags:
                    ApplyProfileV4ToV5(profile);
                    version = ProfileSchemaVersion.FormsTemplates;
                    break;
                default:
                    throw CreateMissingMigrationException($"profile {GetProfileLabel(profile)}", version);
            }
        }

        profile.SchemaVersion = ProfileSchemaVersion.Current;
        profile.Initialize();
    }

    public static void PrepareStoreForPersistence(DeckProfileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        EnsureStoreIsSupportedForPersistence(store);
        MigrateStore(store);
        store.SchemaVersion = ProfileSchemaVersion.Current;

        foreach (var profile in store.Profiles)
            profile.SchemaVersion = ProfileSchemaVersion.Current;
    }

    public static void PrepareProfileForPersistence(DeckProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        EnsureProfileIsSupportedForPersistence(profile);
        MigrateProfile(profile);
        profile.SchemaVersion = ProfileSchemaVersion.Current;
    }

    private static int NormalizeVersion(int version)
    {
        return version <= ProfileSchemaVersion.Unspecified
            ? ProfileSchemaVersion.Baseline
            : version;
    }

    private static void ApplyStoreV1ToV2(DeckProfileStore store)
    {
        foreach (var profile in store.Profiles)
        {
            if (profile.SchemaVersion <= ProfileSchemaVersion.Unspecified)
                profile.SchemaVersion = ProfileSchemaVersion.Baseline;
        }
    }

    private static void ApplyStoreV2ToV3(DeckProfileStore store)
    {
        // Version 3 only changes profile-owned clipboard data.
    }

    private static void ApplyStoreV3ToV4(DeckProfileStore store)
    {
        // Version 4 only changes profile-owned clipboard data.
    }

    private static void ApplyStoreV4ToV5(DeckProfileStore store)
    {
        // Version 5 only adds profile-owned form template data.
    }

    private static void ApplyProfileV1ToV2(DeckProfile profile)
    {
        profile.Pages ??= new List<DeckPage>();
        if (profile.Pages.Count == 0)
            profile.Pages.Add(new DeckPage());

        if (profile.LayoutRows < DeckPage.MinRows || profile.LayoutRows > DeckPage.MaxRows)
            profile.LayoutRows = profile.Pages[0].Rows;

        if (profile.LayoutColumns < DeckPage.MinColumns || profile.LayoutColumns > DeckPage.MaxColumns)
            profile.LayoutColumns = profile.Pages[0].Columns;

        profile.NotePages ??= new List<NotePage>();
        if (profile.NotePages.Count == 0)
        {
            bool hasLegacyNotes = profile.Pages.Any(page => page.StickyNotes is { Count: > 0 });
            if (hasLegacyNotes)
            {
                for (int i = 0; i < profile.Pages.Count; i++)
                {
                    var page = profile.Pages[i];
                    var migratedNotes = page.StickyNotes ?? new List<StickyNote>();

                    profile.NotePages.Add(new NotePage
                    {
                        Name = $"Notes {i + 1}",
                        StickyNotes = migratedNotes
                    });

                    page.StickyNotes = new List<StickyNote>();
                }
            }
        }

        profile.SchemaVersion = ProfileSchemaVersion.StickyNotesAndLayoutMigration;
    }

    private static void ApplyProfileV2ToV3(DeckProfile profile)
    {
        profile.QuickTextCollections ??= new List<QuickTextCollection>();
        profile.QuickTextCategories ??= new List<QuickTextCategory>();
        profile.QuickTextItems ??= new List<QuickTextItem>();

        if (profile.QuickTextCollections.Count == 0)
            profile.QuickTextCollections.Add(new QuickTextCollection { Name = "General" });

        var fallbackCollection = profile.QuickTextCollections[0];
        fallbackCollection.EnsureInitialized();

        foreach (var category in profile.QuickTextCategories)
        {
            category.EnsureInitialized();
            if (string.IsNullOrWhiteSpace(category.CollectionId))
                category.CollectionId = fallbackCollection.Id;
        }

        foreach (var item in profile.QuickTextItems)
        {
            item.EnsureInitialized();
            if (item.CategoryIds.Count == 0 && !string.IsNullOrWhiteSpace(item.CategoryId))
                item.SetCategories(new[] { item.CategoryId });
        }

        profile.ActiveQuickTextCollectionId = profile.QuickTextCategories
            .FirstOrDefault(category => string.Equals(category.Id, profile.ActiveQuickTextCategoryId, StringComparison.Ordinal))?.CollectionId
            ?? fallbackCollection.Id;
        profile.SchemaVersion = ProfileSchemaVersion.QuickTextCollectionsAndTags;
    }

    private static void ApplyProfileV3ToV4(DeckProfile profile)
    {
        profile.QuickTextCollections ??= new List<QuickTextCollection>();
        profile.QuickTextCategories ??= new List<QuickTextCategory>();
        profile.QuickTextItems ??= new List<QuickTextItem>();

        if (profile.QuickTextCollections.Count == 0)
            profile.QuickTextCollections.Add(new QuickTextCollection { Name = "General" });

        foreach (var collection in profile.QuickTextCollections)
            collection.EnsureInitialized();

        var originalTags = profile.QuickTextCategories.ToList();
        var canonicalTags = new List<QuickTextCategory>();
        var canonicalByName = new Dictionary<string, QuickTextCategory>(StringComparer.OrdinalIgnoreCase);
        var tagIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var legacyCollectionByTagId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in originalTags)
        {
            tag.EnsureInitialized();
            legacyCollectionByTagId[tag.Id] = tag.CollectionId;
            string key = tag.Name.Trim();
            if (!canonicalByName.TryGetValue(key, out var canonical))
            {
                canonical = tag;
                canonical.CollectionId = string.Empty;
                canonicalByName[key] = canonical;
                canonicalTags.Add(canonical);
            }

            tagIdMap[tag.Id] = canonical.Id;
        }

        foreach (var item in profile.QuickTextItems)
        {
            item.EnsureInitialized();
            var collectionIds = item.CollectionIds.ToList();
            foreach (string tagId in item.CategoryIds)
            {
                if (legacyCollectionByTagId.TryGetValue(tagId, out string? collectionId)
                    && !string.IsNullOrWhiteSpace(collectionId))
                {
                    collectionIds.Add(collectionId);
                }
            }

            item.SetCategories(item.CategoryIds
                .Where(tagIdMap.ContainsKey)
                .Select(tagId => tagIdMap[tagId]));
            item.SetCollections(collectionIds);
        }

        profile.QuickTextCategories = canonicalTags;
        if (tagIdMap.TryGetValue(profile.ActiveQuickTextCategoryId, out string? activeTagId))
            profile.ActiveQuickTextCategoryId = activeTagId;

        if (string.IsNullOrWhiteSpace(profile.ActiveQuickTextCollectionId)
            || !profile.QuickTextCollections.Any(collection => string.Equals(collection.Id, profile.ActiveQuickTextCollectionId, StringComparison.Ordinal)))
        {
            profile.ActiveQuickTextCollectionId = profile.QuickTextCollections[0].Id;
        }

        profile.SchemaVersion = ProfileSchemaVersion.GlobalQuickTextTags;
    }

    private static void ApplyProfileV4ToV5(DeckProfile profile)
    {
        profile.FormTemplates ??= new List<FormTemplate>();
        profile.ActiveFormTemplateId ??= string.Empty;

        foreach (var template in profile.FormTemplates)
            template.EnsureInitialized();

        profile.SchemaVersion = ProfileSchemaVersion.FormsTemplates;
    }

    private static void EnsureStoreIsSupportedForPersistence(DeckProfileStore store)
    {
        if (store.SchemaVersion > ProfileSchemaVersion.Current)
            throw CreateUnsupportedPersistenceException("profile store", store.SchemaVersion);

        foreach (var profile in store.Profiles)
            EnsureProfileIsSupportedForPersistence(profile);
    }

    private static void EnsureProfileIsSupportedForPersistence(DeckProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SchemaVersion > ProfileSchemaVersion.Current)
            throw CreateUnsupportedPersistenceException($"profile {GetProfileLabel(profile)}", profile.SchemaVersion);
    }

    private static InvalidOperationException CreateMissingMigrationException(string subject, int version)
    {
        return new InvalidOperationException(
            $"No migration path is defined for {subject} schema version {version}. Update StreamDecky before loading this data.");
    }

    private static InvalidOperationException CreateUnsupportedPersistenceException(string subject, int version)
    {
        return new InvalidOperationException(
            $"Cannot save {subject} because it uses schema version {version}, which is newer than this app supports ({ProfileSchemaVersion.Current}). Open the data in a compatible StreamDecky version to avoid overwriting newer fields.");
    }

    private static string GetProfileLabel(DeckProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Name))
            return $"\"{profile.Name}\"";

        if (!string.IsNullOrWhiteSpace(profile.Id))
            return $"\"{profile.Id}\"";

        return "<unnamed>";
    }
}
