namespace StreamDecky.Models;

public class DeckProfile
{
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

        Pages ??= new List<DeckPage>();
        VirtualLayouts ??= new List<DeckPage>();
        NotePages ??= new List<NotePage>();

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

        CurrentNotePageIndex = Math.Clamp(CurrentNotePageIndex, 0, NotePages.Count - 1);
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
