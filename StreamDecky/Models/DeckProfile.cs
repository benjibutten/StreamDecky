namespace StreamDecky.Models;

public class DeckProfile
{
    public const double MinStickyNoteFontSize = 10;
    public const double MaxStickyNoteFontSize = 30;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Standard";
    public string OverlayBackgroundColor { get; set; } = "#1E1E2E";
    public string OverlayBackgroundImagePath { get; set; } = string.Empty;
    public double ButtonOverlayOpacity { get; set; } = 1.0;
    public double ButtonSpacing { get; set; } = 10;
    public double ButtonSize { get; set; } = 100;
    public double GridOffsetX { get; set; } = 0;
    public double GridOffsetY { get; set; } = 0;
    public uint HotkeyModifiers { get; set; } = 0x0002; // MOD_CONTROL
    public uint HotkeyVk { get; set; } = 0x7B;          // VK_F12
    public string HotkeyDisplayText { get; set; } = "Ctrl + F12";
    public bool StartWithWindows { get; set; } = false;
    public bool NaturalTypingEnabled { get; set; } = false;
    public bool GamepadSupportEnabled { get; set; } = false;
    public ushort GamepadToggleButtons { get; set; } = 0x0030; // Back + Start
    public bool StickyNotesVisible { get; set; }
    public double StickyNoteFontSize { get; set; } = 13;
    public List<QuickTextCategory> QuickTextCategories { get; set; } = new() { new QuickTextCategory() };
    public string ActiveQuickTextCategoryId { get; set; } = string.Empty;
    public List<QuickTextItem> QuickTextItems { get; set; } = new();
    public double QuickTextPanelX { get; set; } = 30;
    public double QuickTextPanelY { get; set; } = 96;
    public int LayoutRows { get; set; }
    public int LayoutColumns { get; set; }
    public List<DeckPage> Pages { get; set; } = new() { new DeckPage() };
    public List<DeckPage> VirtualLayouts { get; set; } = new();
    public List<NotePage> NotePages { get; set; } = new() { new NotePage() };
    public int CurrentNotePageIndex { get; set; }

    public void Initialize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "Standard";

        ButtonOverlayOpacity = Math.Clamp(ButtonOverlayOpacity, 0.2, 1.0);
        StickyNoteFontSize = Math.Clamp(StickyNoteFontSize, MinStickyNoteFontSize, MaxStickyNoteFontSize);

        Pages ??= new List<DeckPage>();
        VirtualLayouts ??= new List<DeckPage>();
        NotePages ??= new List<NotePage>();
        QuickTextCategories ??= new List<QuickTextCategory>();
        QuickTextItems ??= new List<QuickTextItem>();

        if (Pages.Count == 0)
            Pages.Add(new DeckPage());

        // Backwards compatibility: old profiles stored rows/columns per page only.
        if (LayoutRows < DeckPage.MinRows || LayoutRows > DeckPage.MaxRows)
            LayoutRows = Pages[0].Rows;

        if (LayoutColumns < DeckPage.MinColumns || LayoutColumns > DeckPage.MaxColumns)
            LayoutColumns = Pages[0].Columns;

        LayoutRows = Math.Clamp(LayoutRows, DeckPage.MinRows, DeckPage.MaxRows);
        LayoutColumns = Math.Clamp(LayoutColumns, DeckPage.MinColumns, DeckPage.MaxColumns);

        foreach (var page in Pages)
            page.EnsureButtonCount(LayoutRows, LayoutColumns);

        foreach (var layout in VirtualLayouts)
            layout.EnsureButtonCount(LayoutRows, LayoutColumns);

        MigrateLegacyStickyNotes();

        if (NotePages.Count == 0)
            NotePages.Add(new NotePage());

        foreach (var notePage in NotePages)
            notePage.EnsureInitialized();

        if (QuickTextCategories.Count == 0)
            QuickTextCategories.Add(new QuickTextCategory { Name = "General" });

        foreach (var category in QuickTextCategories)
            category.EnsureInitialized();

        if (string.IsNullOrWhiteSpace(ActiveQuickTextCategoryId)
            || !QuickTextCategories.Any(category => string.Equals(category.Id, ActiveQuickTextCategoryId, StringComparison.Ordinal)))
        {
            ActiveQuickTextCategoryId = QuickTextCategories[0].Id;
        }

        foreach (var item in QuickTextItems)
        {
            item.EnsureInitialized();

            if (string.IsNullOrWhiteSpace(item.CategoryId)
                || !QuickTextCategories.Any(category => string.Equals(category.Id, item.CategoryId, StringComparison.Ordinal)))
            {
                item.CategoryId = ActiveQuickTextCategoryId;
            }
        }

        CurrentNotePageIndex = Math.Clamp(CurrentNotePageIndex, 0, NotePages.Count - 1);

        QuickTextPanelX = Math.Max(0, QuickTextPanelX);
        QuickTextPanelY = Math.Max(0, QuickTextPanelY);
    }

    private void MigrateLegacyStickyNotes()
    {
        if (NotePages.Count > 0)
            return;

        bool hasLegacyNotes = Pages.Any(page => page.StickyNotes is { Count: > 0 });
        if (!hasLegacyNotes)
            return;

        for (int i = 0; i < Pages.Count; i++)
        {
            var page = Pages[i];
            var migratedNotes = page.StickyNotes ?? new List<StickyNote>();

            NotePages.Add(new NotePage
            {
                Name = $"Notes {i + 1}",
                StickyNotes = migratedNotes
            });

            page.StickyNotes = new List<StickyNote>();
        }
    }
}
