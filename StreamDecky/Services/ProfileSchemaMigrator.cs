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