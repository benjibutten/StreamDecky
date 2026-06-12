namespace StreamDecky.Models;

public static class ProfileSchemaVersion
{
    public const int Unspecified = 0;
    public const int Baseline = 1;
    public const int StickyNotesAndLayoutMigration = 2;
    public const int Current = StickyNotesAndLayoutMigration;
}