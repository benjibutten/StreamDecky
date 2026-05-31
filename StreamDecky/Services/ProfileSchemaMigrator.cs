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
                    version = ProfileSchemaVersion.Baseline;
                    break;
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
                    version = ProfileSchemaVersion.Baseline;
                    break;
            }
        }

        profile.SchemaVersion = ProfileSchemaVersion.Current;
        profile.Initialize();
    }

    public static void PrepareStoreForPersistence(DeckProfileStore store)
    {
        MigrateStore(store);
        store.SchemaVersion = ProfileSchemaVersion.Current;

        foreach (var profile in store.Profiles)
            profile.SchemaVersion = ProfileSchemaVersion.Current;
    }

    public static void PrepareProfileForPersistence(DeckProfile profile)
    {
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
}