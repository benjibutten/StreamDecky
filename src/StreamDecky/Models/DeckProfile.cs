namespace StreamDecky.Models;

public class DeckProfile
{
    public const double MinStickyNoteFontSize = 10;
    public const double MaxStickyNoteFontSize = 30;
    public const double MinQuickTextFontSize = 9;
    public const double MaxQuickTextFontSize = 24;
    public const double MinQuickTextPanelWidth = 280;
    public const double MaxQuickTextPanelWidth = 700;
    public const double MinQuickTextPanelHeight = 180;
    public const double MaxQuickTextPanelHeight = 3000;
    public const double MinMusicWidgetWidth = 300;
    public const double MaxMusicWidgetWidth = 700;
    public const double MinMusicWidgetHeight = 260;
    public const double MaxMusicWidgetHeight = 3000;
    public const double MinTextHelperWidth = 280;
    public const double MaxTextHelperWidth = 700;
    public const double MinTextHelperHeight = 200;
    public const double MaxTextHelperHeight = 900;
    public const double MinTextHelperFontSize = 12;
    public const double MaxTextHelperFontSize = 30;
    public const string DefaultTextHelperFontFamily = "Verdana";
    public const double MinFormsPanelWidth = 300;
    public const double MaxFormsPanelWidth = 700;
    public const double MinFormsPanelHeight = 220;
    public const double MaxFormsPanelHeight = 3000;

    public int SchemaVersion { get; set; }
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
    public List<QuickTextCollection> QuickTextCollections { get; set; } = new();
    public string ActiveQuickTextCollectionId { get; set; } = string.Empty;
    public List<QuickTextCategory> QuickTextCategories { get; set; } = new() { new QuickTextCategory() };
    public string ActiveQuickTextCategoryId { get; set; } = string.Empty;
    public List<QuickTextItem> QuickTextItems { get; set; } = new();
    public double QuickTextPanelX { get; set; } = 30;
    public double QuickTextPanelY { get; set; } = 96;
    public double QuickTextPanelWidth { get; set; } = 420;
    public double QuickTextPanelHeight { get; set; } = 380;
    public double QuickTextFontSize { get; set; } = 12;
    public List<ActionStep> QuickTextActionSteps { get; set; } = new();
    public List<FormTemplate> FormTemplates { get; set; } = new();
    public string ActiveFormTemplateId { get; set; } = string.Empty;
    public bool FormsPanelVisible { get; set; }
    public double FormsPanelX { get; set; } = 30;
    public double FormsPanelY { get; set; } = 96;
    public double FormsPanelWidth { get; set; } = 380;
    public double FormsPanelHeight { get; set; } = 460;
    public bool FormsHistoryCountsTodayOnly { get; set; } = true;
    public bool MusicWidgetVisible { get; set; }
    public bool MusicWidgetMinimized { get; set; }
    public double MusicWidgetX { get; set; } = 30;
    public double MusicWidgetY { get; set; } = 96;
    public double MusicWidgetWidth { get; set; } = 380;
    public double MusicWidgetHeight { get; set; } = 540;
    public bool TextHelperVisible { get; set; }
    public double TextHelperX { get; set; } = 30;
    public double TextHelperY { get; set; } = 96;
    public double TextHelperWidth { get; set; } = 420;
    public double TextHelperHeight { get; set; } = 320;
    public bool TextHelperDarkTextArea { get; set; }
    public string TextHelperFontFamily { get; set; } = DefaultTextHelperFontFamily;
    public double TextHelperFontSize { get; set; } = 17;
    public int LayoutRows { get; set; } = 3;
    public int LayoutColumns { get; set; } = 5;
    public List<DeckPage> Pages { get; set; } = new() { new DeckPage() };
    public List<DeckPage> VirtualLayouts { get; set; } = new();
    public List<NotePage> NotePages { get; set; } = new();
    public int CurrentNotePageIndex { get; set; }

    public void Initialize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "Standard";

        ButtonOverlayOpacity = Math.Clamp(ButtonOverlayOpacity, 0.2, 1.0);
        StickyNoteFontSize = Math.Clamp(StickyNoteFontSize, MinStickyNoteFontSize, MaxStickyNoteFontSize);
        QuickTextFontSize = Math.Clamp(QuickTextFontSize == 0 ? 12 : QuickTextFontSize, MinQuickTextFontSize, MaxQuickTextFontSize);
        QuickTextPanelWidth = Math.Clamp(QuickTextPanelWidth == 0 ? 420 : QuickTextPanelWidth, MinQuickTextPanelWidth, MaxQuickTextPanelWidth);
        QuickTextPanelHeight = Math.Clamp(QuickTextPanelHeight == 0 ? 380 : QuickTextPanelHeight, MinQuickTextPanelHeight, MaxQuickTextPanelHeight);

        QuickTextActionSteps ??= new List<ActionStep>();

        Pages ??= new List<DeckPage>();
        VirtualLayouts ??= new List<DeckPage>();
        NotePages ??= new List<NotePage>();
        QuickTextCollections ??= new List<QuickTextCollection>();
        QuickTextCategories ??= new List<QuickTextCategory>();
        QuickTextItems ??= new List<QuickTextItem>();
        FormTemplates ??= new List<FormTemplate>();

        if (Pages.Count == 0)
            Pages.Add(new DeckPage());

        LayoutRows = Math.Clamp(LayoutRows == 0 ? 3 : LayoutRows, DeckPage.MinRows, DeckPage.MaxRows);
        LayoutColumns = Math.Clamp(LayoutColumns == 0 ? 5 : LayoutColumns, DeckPage.MinColumns, DeckPage.MaxColumns);

        foreach (var page in Pages)
            page.EnsureButtonCount(LayoutRows, LayoutColumns);

        foreach (var layout in VirtualLayouts)
            layout.EnsureButtonCount(LayoutRows, LayoutColumns);

        if (NotePages.Count == 0)
            NotePages.Add(new NotePage());

        foreach (var notePage in NotePages)
            notePage.EnsureInitialized();

        if (QuickTextCollections.Count == 0)
            QuickTextCollections.Add(new QuickTextCollection { Name = "General" });

        foreach (var collection in QuickTextCollections)
            collection.EnsureInitialized();

        if (QuickTextCategories.Count == 0)
            QuickTextCategories.Add(new QuickTextCategory { Name = "General" });

        foreach (var category in QuickTextCategories)
            category.EnsureInitialized();

        if (string.IsNullOrWhiteSpace(ActiveQuickTextCollectionId)
            || !QuickTextCollections.Any(collection => string.Equals(collection.Id, ActiveQuickTextCollectionId, StringComparison.Ordinal)))
        {
            ActiveQuickTextCollectionId = QuickTextCollections[0].Id;
        }

        if (string.IsNullOrWhiteSpace(ActiveQuickTextCategoryId)
            || !QuickTextCategories.Any(category => string.Equals(category.Id, ActiveQuickTextCategoryId, StringComparison.Ordinal)))
        {
            ActiveQuickTextCategoryId = QuickTextCategories[0].Id;
        }

        foreach (var item in QuickTextItems)
        {
            item.EnsureInitialized();
            var validCategoryIds = item.CategoryIds
                .Where(id => QuickTextCategories.Any(category => string.Equals(category.Id, id, StringComparison.Ordinal)))
                .ToList();

            if (validCategoryIds.Count == 0)
                validCategoryIds.Add(ActiveQuickTextCategoryId);

            item.SetCategories(validCategoryIds);

            var validCollectionIds = item.CollectionIds
                .Where(id => QuickTextCollections.Any(collection => string.Equals(collection.Id, id, StringComparison.Ordinal)))
                .ToList();
            if (validCollectionIds.Count == 0)
                validCollectionIds.Add(ActiveQuickTextCollectionId);

            item.SetCollections(validCollectionIds);
        }

        CurrentNotePageIndex = Math.Clamp(CurrentNotePageIndex, 0, NotePages.Count - 1);

        QuickTextPanelX = Math.Max(0, QuickTextPanelX);
        QuickTextPanelY = Math.Max(0, QuickTextPanelY);

        foreach (var template in FormTemplates)
            template.EnsureInitialized();

        ActiveFormTemplateId ??= string.Empty;
        if (!string.IsNullOrWhiteSpace(ActiveFormTemplateId)
            && !FormTemplates.Any(template => string.Equals(template.Id, ActiveFormTemplateId, StringComparison.Ordinal)))
        {
            ActiveFormTemplateId = FormTemplates.FirstOrDefault()?.Id ?? string.Empty;
        }

        FormsPanelWidth = Math.Clamp(FormsPanelWidth == 0 ? 380 : FormsPanelWidth, MinFormsPanelWidth, MaxFormsPanelWidth);
        FormsPanelHeight = Math.Clamp(FormsPanelHeight == 0 ? 460 : FormsPanelHeight, MinFormsPanelHeight, MaxFormsPanelHeight);
        FormsPanelX = Math.Max(0, FormsPanelX);
        FormsPanelY = Math.Max(0, FormsPanelY);

        MusicWidgetWidth = Math.Clamp(MusicWidgetWidth == 0 ? 380 : MusicWidgetWidth, MinMusicWidgetWidth, MaxMusicWidgetWidth);
        MusicWidgetHeight = Math.Clamp(MusicWidgetHeight == 0 ? 540 : MusicWidgetHeight, MinMusicWidgetHeight, MaxMusicWidgetHeight);
        MusicWidgetX = Math.Max(0, MusicWidgetX);
        MusicWidgetY = Math.Max(0, MusicWidgetY);

        TextHelperWidth = Math.Clamp(TextHelperWidth == 0 ? 420 : TextHelperWidth, MinTextHelperWidth, MaxTextHelperWidth);
        TextHelperHeight = Math.Clamp(TextHelperHeight == 0 ? 320 : TextHelperHeight, MinTextHelperHeight, MaxTextHelperHeight);
        TextHelperX = Math.Max(0, TextHelperX);
        TextHelperY = Math.Max(0, TextHelperY);
        TextHelperFontSize = Math.Clamp(
            TextHelperFontSize == 0 ? 17 : TextHelperFontSize,
            MinTextHelperFontSize,
            MaxTextHelperFontSize);

        if (string.IsNullOrWhiteSpace(TextHelperFontFamily))
            TextHelperFontFamily = DefaultTextHelperFontFamily;
    }
}
