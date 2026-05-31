using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;
using StreamDecky.Models;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions ProfileCloneJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ProfileService _profileService;
    private readonly TextInputActionService _textInputService;
    private readonly MultiActionService _multiActionService;
    private readonly System.Timers.Timer _autoSaveTimer;
    private readonly SemaphoreSlim _autoSaveSemaphore = new(1, 1);
    private readonly List<ButtonViewModel> _trackedButtons = new();

    private DeckProfileStore _profileStore;
    private DeckProfile _profile;
    private ButtonConfig? _buttonClipboard;
    private int _currentVirtualLayoutIndex = -1;

    public ObservableCollection<LayoutTargetOption> LayoutTargets { get; } = new();
    public ObservableCollection<ProfileOption> ProfileOptions { get; } = new();

    public MainViewModel(
        ProfileService? profileService = null,
        TextInputActionService? textInputService = null,
        MultiActionService? multiActionService = null)
    {
        _profileService = profileService ?? new ProfileService();
        _textInputService = textInputService ?? new TextInputActionService();
        _multiActionService = multiActionService ?? new MultiActionService();

        // Auto-save: debounce 1 second after last change
        _autoSaveTimer = new System.Timers.Timer(1000) { AutoReset = false };
        _autoSaveTimer.Elapsed += (_, _) => _ = AutoSaveAsync();

        _profileStore = _profileService.LoadStore();
        _profile = _profileStore.GetActiveProfile();
        _currentPageIndex = 0;
        _currentNotePageIndex = Math.Clamp(_profile.CurrentNotePageIndex, 0, _profile.NotePages.Count - 1);

        LoadCurrentLayout();
        StickyNotesVisible = true;
        LoadQuickTextCategories();
        LoadQuickTextActionSteps();

        RebuildProfileOptions();
        RebuildLayoutTargets();
        SyncSelectedProfileId();
        SyncSelectedLayoutId();

        _ = RefreshOverlayBackgroundImageAsync();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    [ObservableProperty]
    private ObservableCollection<ButtonViewModel> _buttons = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsButtonSelected))]
    private ButtonViewModel? _selectedButton;

    [ObservableProperty]
    private int _currentPageIndex;

    [ObservableProperty]
    private int _currentNotePageIndex;

    [ObservableProperty]
    private bool _isOverlayOpen;

    [ObservableProperty]
    private int _buttonVisualVersion;

    [ObservableProperty]
    private int _overlayBackgroundImageVersion;

    [ObservableProperty]
    private ObservableCollection<StickyNoteViewModel> _stickyNotes = new();

    [ObservableProperty]
    private ObservableCollection<QuickTextItemViewModel> _quickTextItems = new();

    [ObservableProperty]
    private ObservableCollection<QuickTextCategory> _quickTextCategories = new();

    [ObservableProperty]
    private bool _stickyNotesVisible;

    [ObservableProperty]
    private bool _isClipboardEditorMode;

    [ObservableProperty]
    private string _quickTextSearchQuery = string.Empty;

    [ObservableProperty]
    private string? _selectedProfileId;

    [ObservableProperty]
    private string? _selectedLayoutId;

    [ObservableProperty]
    private string? _selectedQuickTextCategoryId;

    partial void OnCurrentNotePageIndexChanged(int value)
    {
        if (_profile.NotePages.Count == 0)
            return;

        int clamped = Math.Clamp(value, 0, _profile.NotePages.Count - 1);
        if (clamped != value)
        {
            CurrentNotePageIndex = clamped;
            return;
        }

        _profile.CurrentNotePageIndex = clamped;
        LoadStickyNotes();
        NotifyNotePageChanged();
        ScheduleAutoSave();
    }

    partial void OnSelectedLayoutIdChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SwitchToLayoutById(value);
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SwitchToProfileById(value);
    }

    partial void OnIsClipboardEditorModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsButtonEditorMode));
    }

    partial void OnSelectedQuickTextCategoryIdChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!_profile.QuickTextCategories.Any(category => string.Equals(category.Id, value, StringComparison.Ordinal)))
        {
            if (QuickTextCategories.Count > 0)
                SelectedQuickTextCategoryId = QuickTextCategories[0].Id;
            return;
        }

        if (!string.Equals(_profile.ActiveQuickTextCategoryId, value, StringComparison.Ordinal))
        {
            _profile.ActiveQuickTextCategoryId = value;
            ScheduleAutoSave();
        }

        LoadQuickTextItemsForSelectedCategory();
        NotifyQuickTextCategoryChanged();
    }

    partial void OnQuickTextSearchQueryChanged(string value)
    {
        LoadQuickTextItemsForSelectedCategory();
    }

    public bool IsButtonSelected => SelectedButton != null;
    public bool IsButtonEditorMode => !IsClipboardEditorMode;
    public bool IsViewingVirtualLayout => _currentVirtualLayoutIndex >= 0;
    public bool HasVirtualLayouts => _profile.VirtualLayouts.Count > 0;
    public string CurrentLayoutKindLabel => IsViewingVirtualLayout ? "Virtual Layout" : "Page";
    public bool CanRemoveCurrentVirtualLayout => IsViewingVirtualLayout && _profile.VirtualLayouts.Count > 0;

    public DeckProfile Profile => _profile;
    public string ActiveProfileName => _profile.Name;
    public int ProfileCount => _profileStore.Profiles.Count;
    public bool CanRemoveProfile => ProfileCount > 1;
    public string ProfileIndicator => $"{GetActiveProfileIndex() + 1} / {ProfileCount}";

    public string OverlayBackgroundColor
    {
        get => _profile.OverlayBackgroundColor;
        set
        {
            if (string.Equals(_profile.OverlayBackgroundColor, value, StringComparison.OrdinalIgnoreCase))
                return;

            _profile.OverlayBackgroundColor = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public string OverlayBackgroundImagePath
    {
        get => _profile.OverlayBackgroundImagePath;
        set
        {
            if (string.Equals(_profile.OverlayBackgroundImagePath, value, StringComparison.OrdinalIgnoreCase))
                return;

            _profile.OverlayBackgroundImagePath = value;
            OnPropertyChanged();
            _ = RefreshOverlayBackgroundImageAsync();
            ScheduleAutoSave();
        }
    }

    public double ButtonOverlayOpacity
    {
        get => _profile.ButtonOverlayOpacity;
        set
        {
            double clamped = Math.Clamp(value, 0.2, 1.0);
            if (Math.Abs(_profile.ButtonOverlayOpacity - clamped) < 0.001)
                return;

            _profile.ButtonOverlayOpacity = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double ButtonSpacing
    {
        get => _profile.ButtonSpacing;
        set
        {
            if (Math.Abs(_profile.ButtonSpacing - value) < 0.001)
                return;

            _profile.ButtonSpacing = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double ButtonSize
    {
        get => _profile.ButtonSize;
        set
        {
            if (Math.Abs(_profile.ButtonSize - value) < 0.001)
                return;

            _profile.ButtonSize = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double GridOffsetX
    {
        get => _profile.GridOffsetX;
        set
        {
            if (Math.Abs(_profile.GridOffsetX - value) < 0.001)
                return;

            _profile.GridOffsetX = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double QuickTextPanelX
    {
        get => _profile.QuickTextPanelX;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.QuickTextPanelX - clamped) < 0.001)
                return;

            _profile.QuickTextPanelX = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double QuickTextPanelY
    {
        get => _profile.QuickTextPanelY;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.QuickTextPanelY - clamped) < 0.001)
                return;

            _profile.QuickTextPanelY = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double GridOffsetY
    {
        get => _profile.GridOffsetY;
        set
        {
            if (Math.Abs(_profile.GridOffsetY - value) < 0.001)
                return;

            _profile.GridOffsetY = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public uint HotkeyModifiers
    {
        get => _profile.HotkeyModifiers;
        set
        {
            if (_profile.HotkeyModifiers == value)
                return;

            _profile.HotkeyModifiers = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplayText));
            ScheduleAutoSave();
        }
    }

    public uint HotkeyVk
    {
        get => _profile.HotkeyVk;
        set
        {
            if (_profile.HotkeyVk == value)
                return;

            _profile.HotkeyVk = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplayText));
            ScheduleAutoSave();
        }
    }

    public string HotkeyDisplayText
    {
        get => _profile.HotkeyDisplayText;
        set
        {
            if (string.Equals(_profile.HotkeyDisplayText, value, StringComparison.Ordinal))
                return;

            _profile.HotkeyDisplayText = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public bool StartWithWindows
    {
        get => _profile.StartWithWindows;
        set
        {
            if (_profile.StartWithWindows == value)
                return;

            _profile.StartWithWindows = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public bool NaturalTypingEnabled
    {
        get => _profile.NaturalTypingEnabled;
        set
        {
            if (_profile.NaturalTypingEnabled == value)
                return;

            _profile.NaturalTypingEnabled = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public bool GamepadSupportEnabled
    {
        get => _profile.GamepadSupportEnabled;
        set
        {
            if (_profile.GamepadSupportEnabled == value)
                return;

            _profile.GamepadSupportEnabled = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double StickyNoteFontSize
    {
        get => _profile.StickyNoteFontSize;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinStickyNoteFontSize, DeckProfile.MaxStickyNoteFontSize);
            if (Math.Abs(_profile.StickyNoteFontSize - clamped) < 0.001)
                return;

            _profile.StickyNoteFontSize = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double QuickTextFontSize
    {
        get => _profile.QuickTextFontSize;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinQuickTextFontSize, DeckProfile.MaxQuickTextFontSize);
            if (Math.Abs(_profile.QuickTextFontSize - clamped) < 0.001)
                return;

            _profile.QuickTextFontSize = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickTextPreviewLineHeight));
            OnPropertyChanged(nameof(QuickTextPreviewHeight));
            OnPropertyChanged(nameof(QuickTextEditorHeight));
            OnPropertyChanged(nameof(QuickTextHintLineHeight));
            OnPropertyChanged(nameof(QuickTextHintMaxHeight));
            ScheduleAutoSave();
        }
    }

    public double QuickTextPreviewLineHeight => Math.Max(16, QuickTextFontSize + 4);

    public double QuickTextPreviewHeight => Math.Round(QuickTextPreviewLineHeight * 2);

    public double QuickTextEditorHeight => Math.Round((QuickTextPreviewLineHeight * 2) + 8);

    public double QuickTextHintLineHeight => Math.Max(14, QuickTextFontSize + 2);

    public double QuickTextHintMaxHeight => Math.Round(QuickTextHintLineHeight * 2);

    public double QuickTextPanelWidth
    {
        get => _profile.QuickTextPanelWidth;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinQuickTextPanelWidth, DeckProfile.MaxQuickTextPanelWidth);
            if (Math.Abs(_profile.QuickTextPanelWidth - clamped) < 0.001)
                return;

            _profile.QuickTextPanelWidth = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double QuickTextPanelHeight
    {
        get => _profile.QuickTextPanelHeight;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinQuickTextPanelHeight, DeckProfile.MaxQuickTextPanelHeight);
            if (Math.Abs(_profile.QuickTextPanelHeight - clamped) < 0.001)
                return;

            _profile.QuickTextPanelHeight = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public bool HasQuickTextAction => _profile.QuickTextActionSteps.Count > 0;

    public ObservableCollection<ActionStep> QuickTextActionSteps { get; private set; } = new();

    public ushort GamepadToggleButtons
    {
        get => _profile.GamepadToggleButtons;
        set
        {
            if (value == 0 || _profile.GamepadToggleButtons == value)
                return;

            _profile.GamepadToggleButtons = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GamepadToggleDisplayText));
            ScheduleAutoSave();
        }
    }

    public string GamepadToggleDisplayText => FormatGamepadButtons(GamepadToggleButtons);

    private static string FormatGamepadButtons(ushort buttons)
    {
        if (buttons == 0)
            return "None";

        var parts = new List<string>();

        AddPartIfPressed(parts, buttons, XInputInterop.GamepadBack, "Back");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadStart, "Start");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadLeftShoulder, "LB");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadRightShoulder, "RB");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadLeftThumb, "L3");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadRightThumb, "R3");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadDPadUp, "DPad Up");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadDPadDown, "DPad Down");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadDPadLeft, "DPad Left");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadDPadRight, "DPad Right");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadA, "A");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadB, "B");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadX, "X");
        AddPartIfPressed(parts, buttons, XInputInterop.GamepadY, "Y");

        return parts.Count > 0 ? string.Join(" + ", parts) : "Unknown";
    }

    private static void AddPartIfPressed(List<string> parts, ushort buttons, ushort flag, string label)
    {
        if (XInputInterop.IsButtonPressed(buttons, flag))
            parts.Add(label);
    }

    private int GetActiveProfileIndex()
    {
        int index = _profileStore.Profiles.FindIndex(profile => string.Equals(profile.Id, _profile.Id, StringComparison.Ordinal));
        return index >= 0 ? index : 0;
    }

    private string CreateUniqueProfileName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Ny profil" : baseName.Trim();

        if (_profileStore.Profiles.All(profile => !string.Equals(profile.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int suffix = 2;
        while (_profileStore.Profiles.Any(profile => string.Equals(profile.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;

        return $"{baseName} {suffix}";
    }

    public int Rows
    {
        get => _profile.LayoutRows;
        set => UpdateProfileLayout(value, Columns);
    }

    public int Columns
    {
        get => _profile.LayoutColumns;
        set => UpdateProfileLayout(Rows, value);
    }

    public int MinRows => DeckPage.MinRows;
    public int MaxRows => DeckPage.MaxRows;
    public int MinColumns => DeckPage.MinColumns;
    public int MaxColumns => DeckPage.MaxColumns;
    public int MaxButtonsPerPage => DeckPage.MaxButtonsPerPage;
    public int ButtonSlots => Rows * Columns;
    public string LayoutSummary => $"{Rows} x {Columns} ({ButtonSlots} slots)";

    public string CurrentPageName => CurrentLayout.Name;

    public int PageCount => _profile.Pages.Count;
    public bool CanGoToPreviousPage => !IsViewingVirtualLayout && CurrentPageIndex > 0;
    public bool CanGoToNextPage => !IsViewingVirtualLayout && CurrentPageIndex < _profile.Pages.Count - 1;
    public string PageIndicator => IsViewingVirtualLayout
        ? $"V {_currentVirtualLayoutIndex + 1} / {_profile.VirtualLayouts.Count}"
        : $"{CurrentPageIndex + 1} / {PageCount}";

    public bool HasMultiplePages => !IsViewingVirtualLayout && _profile.Pages.Count > 1;

    public string CurrentNotePageName => CurrentNotePage.Name;
    public int NotePageCount => _profile.NotePages.Count;
    public bool CanGoToPreviousNotePage => CurrentNotePageIndex > 0;
    public bool CanGoToNextNotePage => CurrentNotePageIndex < _profile.NotePages.Count - 1;
    public bool CanRemoveNotePage => NotePageCount > 1;
    public string NotePageIndicator => $"{CurrentNotePageIndex + 1} / {NotePageCount}";
    public bool HasMultipleNotePages => NotePageCount > 1;
    public int CurrentNotePageNoteCount => CurrentNotePage.StickyNotes.Count;

    public bool HasStickyNotes => StickyNotes.Count > 0;
    public bool HasQuickTextItems => QuickTextItems.Count > 0;
    public bool HasAnyQuickTextItems => _profile.QuickTextItems.Count > 0;
    public int QuickTextCategoryCount => QuickTextCategories.Count;
    public bool HasMultipleQuickTextCategories => QuickTextCategoryCount > 1;
    public string CurrentQuickTextCategoryName => CurrentQuickTextCategory?.Name ?? "General";
    public bool CanGoToPreviousQuickTextCategory => CurrentQuickTextCategoryIndex > 0;
    public bool CanGoToNextQuickTextCategory => CurrentQuickTextCategoryIndex < QuickTextCategoryCount - 1;

    private DeckPage CurrentRegularPage => _profile.Pages[Math.Clamp(CurrentPageIndex, 0, _profile.Pages.Count - 1)];
    private DeckPage CurrentVirtualLayout
    {
        get
        {
            if (_profile.VirtualLayouts.Count == 0)
                return CurrentRegularPage;

            int index = Math.Clamp(_currentVirtualLayoutIndex, 0, _profile.VirtualLayouts.Count - 1);
            return _profile.VirtualLayouts[index];
        }
    }

    private DeckPage CurrentLayout => IsViewingVirtualLayout && _profile.VirtualLayouts.Count > 0
        ? CurrentVirtualLayout
        : CurrentRegularPage;
    private NotePage CurrentNotePage => _profile.NotePages[Math.Clamp(CurrentNotePageIndex, 0, _profile.NotePages.Count - 1)];
    private int CurrentQuickTextCategoryIndex => QuickTextCategories
        .Select((category, index) => new { category, index })
        .FirstOrDefault(entry => string.Equals(entry.category.Id, SelectedQuickTextCategoryId, StringComparison.Ordinal))?.index ?? 0;
    private QuickTextCategory? CurrentQuickTextCategory => QuickTextCategories.Count == 0
        ? null
        : QuickTextCategories[Math.Clamp(CurrentQuickTextCategoryIndex, 0, QuickTextCategories.Count - 1)];

    private void LoadCurrentLayout(int? preferredSelectedIndex = null)
    {
        DetachButtonHandlers();

        int? selectedIndex = preferredSelectedIndex ?? SelectedButton?.Index;

        CurrentLayout.EnsureButtonCount(Rows, Columns);
        var buttonViewModels = new List<ButtonViewModel>(CurrentLayout.Buttons.Count);
        for (int i = 0; i < CurrentLayout.Buttons.Count; i++)
        {
            var bvm = new ButtonViewModel(CurrentLayout.Buttons[i], i);
            AttachButtonHandlers(bvm);
            buttonViewModels.Add(bvm);
        }

        Buttons = new ObservableCollection<ButtonViewModel>(buttonViewModels);

        ButtonVisualVersion++;

        if (selectedIndex.HasValue && selectedIndex.Value >= 0 && selectedIndex.Value < Buttons.Count)
        {
            SelectButton(Buttons[selectedIndex.Value]);
        }
        else
        {
            if (SelectedButton != null)
                SelectedButton.IsSelected = false;
            SelectedButton = null;
        }

        LoadStickyNotes();
        NotifyLayoutChanged();
        SyncSelectedLayoutId();
    }

    private void LoadStickyNotes()
    {
        var noteViewModels = new List<StickyNoteViewModel>(CurrentNotePage.StickyNotes.Count);
        foreach (var note in CurrentNotePage.StickyNotes)
        {
            noteViewModels.Add(new StickyNoteViewModel(note, ScheduleAutoSave));
        }

        StickyNotes = new ObservableCollection<StickyNoteViewModel>(noteViewModels);

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
    }

    private void LoadQuickTextCategories()
    {
        var categoryList = new List<QuickTextCategory>(_profile.QuickTextCategories.Count);
        foreach (var category in _profile.QuickTextCategories)
        {
            category.EnsureInitialized();
            categoryList.Add(category);
        }

        if (categoryList.Count == 0)
        {
            var fallback = new QuickTextCategory { Name = "General" };
            fallback.EnsureInitialized();
            _profile.QuickTextCategories.Add(fallback);
            categoryList.Add(fallback);
        }

        QuickTextCategories = new ObservableCollection<QuickTextCategory>(categoryList);

        string targetCategoryId = _profile.ActiveQuickTextCategoryId;
        if (string.IsNullOrWhiteSpace(targetCategoryId)
            || !QuickTextCategories.Any(category => string.Equals(category.Id, targetCategoryId, StringComparison.Ordinal)))
        {
            targetCategoryId = QuickTextCategories[0].Id;
            _profile.ActiveQuickTextCategoryId = targetCategoryId;
        }

        if (!string.Equals(SelectedQuickTextCategoryId, targetCategoryId, StringComparison.Ordinal))
        {
            SelectedQuickTextCategoryId = targetCategoryId;
        }
        else
        {
            LoadQuickTextItemsForSelectedCategory();
            NotifyQuickTextCategoryChanged();
        }
    }

    private void LoadQuickTextItemsForSelectedCategory()
    {
        string categoryId = string.IsNullOrWhiteSpace(SelectedQuickTextCategoryId)
            ? _profile.ActiveQuickTextCategoryId
            : SelectedQuickTextCategoryId;

        string query = QuickTextSearchQuery?.Trim() ?? string.Empty;
        bool hasQuery = !string.IsNullOrWhiteSpace(query);

        var quickTextViewModels = new List<QuickTextItemViewModel>();
        foreach (var item in _profile.QuickTextItems)
        {
            if (!hasQuery && !string.Equals(item.CategoryId, categoryId, StringComparison.Ordinal))
                continue;

            if (hasQuery && (item.Text?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                continue;

            quickTextViewModels.Add(new QuickTextItemViewModel(item, ScheduleAutoSave));
        }

        QuickTextItems = new ObservableCollection<QuickTextItemViewModel>(quickTextViewModels);
        OnPropertyChanged(nameof(HasQuickTextItems));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
    }

    private void NotifyQuickTextCategoryChanged()
    {
        OnPropertyChanged(nameof(CurrentQuickTextCategoryName));
        OnPropertyChanged(nameof(QuickTextCategoryCount));
        OnPropertyChanged(nameof(HasMultipleQuickTextCategories));
        OnPropertyChanged(nameof(CanGoToPreviousQuickTextCategory));
        OnPropertyChanged(nameof(CanGoToNextQuickTextCategory));
    }

    private string CreateUniqueQuickTextCategoryName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Category" : baseName.Trim();

        if (_profile.QuickTextCategories.All(category => !string.Equals(category.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int suffix = 2;
        while (_profile.QuickTextCategories.Any(category => string.Equals(category.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;

        return $"{baseName} {suffix}";
    }

    private void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(ButtonSlots));
        OnPropertyChanged(nameof(LayoutSummary));
    }

    private void AttachButtonHandlers(ButtonViewModel buttonViewModel)
    {
        buttonViewModel.PropertyChanged += OnButtonPropertyChanged;
        buttonViewModel.Steps.CollectionChanged += OnButtonStepsCollectionChanged;

        foreach (var step in buttonViewModel.Steps)
            step.PropertyChanged += OnActionStepPropertyChanged;

        _trackedButtons.Add(buttonViewModel);
    }

    private void DetachButtonHandlers()
    {
        foreach (var buttonViewModel in _trackedButtons)
        {
            buttonViewModel.PropertyChanged -= OnButtonPropertyChanged;
            buttonViewModel.Steps.CollectionChanged -= OnButtonStepsCollectionChanged;

            foreach (var step in buttonViewModel.Steps)
                step.PropertyChanged -= OnActionStepPropertyChanged;
        }

        _trackedButtons.Clear();
    }

    private void OnButtonPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleAutoSave();

        if (e.PropertyName is nameof(ButtonViewModel.IsConfigured)
            or nameof(ButtonViewModel.ActionType)
            or nameof(ButtonViewModel.Title)
            or nameof(ButtonViewModel.IconText)
            or nameof(ButtonViewModel.ImagePath)
            or nameof(ButtonViewModel.Shape)
            or null)
        {
            ButtonVisualVersion++;
        }
    }

    private void OnButtonStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems.OfType<ActionStep>())
                oldItem.PropertyChanged -= OnActionStepPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems.OfType<ActionStep>())
                newItem.PropertyChanged += OnActionStepPropertyChanged;
        }

        ScheduleAutoSave();
    }

    private void OnActionStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleAutoSave();
    }

    private async Task AutoSaveAsync()
    {
        bool hasAutoSaveLock = false;

        try
        {
            await _autoSaveSemaphore.WaitAsync().ConfigureAwait(false);
            hasAutoSaveLock = true;

            string json;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                json = await dispatcher.InvokeAsync(() => _profileService.SerializeStore(_profileStore));
            }
            else
            {
                json = _profileService.SerializeStore(_profileStore);
            }

            await _profileService.SaveStoreSerializedAsync(json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Autosave failed.", ex);
        }
        finally
        {
            if (hasAutoSaveLock)
            {
                try
                {
                    _autoSaveSemaphore.Release();
                }
                catch
                {
                    // Ignore release errors during application shutdown.
                }
            }
        }
    }

    private void UpdateProfileLayout(int rows, int columns)
    {
        rows = Math.Clamp(rows, MinRows, MaxRows);
        columns = Math.Clamp(columns, MinColumns, MaxColumns);

        if (_profile.LayoutRows == rows && _profile.LayoutColumns == columns)
            return;

        int? selectedIndex = SelectedButton?.Index;
        _profile.LayoutRows = rows;
        _profile.LayoutColumns = columns;

        foreach (var page in _profile.Pages)
            page.EnsureButtonCount(rows, columns);

        foreach (var layout in _profile.VirtualLayouts)
            layout.EnsureButtonCount(rows, columns);

        LoadCurrentLayout(selectedIndex);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void SelectButton(ButtonViewModel? button)
    {
        // Deselect previous
        if (SelectedButton != null)
            SelectedButton.IsSelected = false;

        SelectedButton = button;

        // Select new
        if (SelectedButton != null)
            SelectedButton.IsSelected = true;
    }

    [RelayCommand]
    private void SelectButtonAndShowEditor(ButtonViewModel? button)
    {
        SelectButton(button);
        ShowButtonEditor();
    }

    [RelayCommand]
    private void OpenOverlay()
    {
        _ = RefreshOverlayBackgroundImageAsync();
        IsOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        IsOverlayOpen = false;
    }

    [RelayCommand]
    private async Task ExecuteButton(ButtonViewModel button)
    {
        if (button.ActionType == ActionType.None)
            return;

        bool closesOverlay = button.ActionType is ActionType.TextInput or ActionType.KeyPress or ActionType.MultiAction;
        if (closesOverlay)
            IsOverlayOpen = false;

        switch (button.ActionType)
        {
            case ActionType.TextInput:
                _textInputService.Execute(button.Config, NaturalTypingEnabled);
                break;
            case ActionType.KeyPress:
                _multiActionService.ExecuteKeyPress(button.Config.KeyText);
                break;
            case ActionType.MultiAction:
                await _multiActionService.ExecuteAsync(button.Config, NaturalTypingEnabled);
                break;
            case ActionType.LayoutNavigation:
                SwitchToLayoutById(button.Config.TargetLayoutId);
                break;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _profileService.SaveStore(_profileStore);
    }

    [RelayCommand]
    private void AddProfile()
    {
        var newProfile = new DeckProfile
        {
            Name = CreateUniqueProfileName("Ny profil")
        };
        newProfile.Initialize();

        _profileStore.Profiles.Add(newProfile);
        _profileStore.ActiveProfileId = newProfile.Id;

        _ = SwitchToProfileById(newProfile.Id);
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        DeckProfile duplicate;
        try
        {
            duplicate = CloneProfile(_profile);
        }
        catch
        {
            return;
        }

        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.Name = CreateUniqueProfileName($"{_profile.Name} Copy");
        duplicate.Initialize();

        _profileStore.Profiles.Add(duplicate);
        _profileStore.ActiveProfileId = duplicate.Id;

        _ = SwitchToProfileById(duplicate.Id);
    }

    [RelayCommand]
    private void ImportProfile(DeckProfile? importedProfile)
    {
        if (importedProfile == null)
            return;

        importedProfile.Initialize();
        importedProfile.Id = Guid.NewGuid().ToString("N");

        string preferredName = string.IsNullOrWhiteSpace(importedProfile.Name)
            ? "Imported Profile"
            : importedProfile.Name.Trim();

        importedProfile.Name = CreateUniqueProfileName(preferredName);
        importedProfile.Initialize();

        _profileStore.Profiles.Add(importedProfile);
        _profileStore.ActiveProfileId = importedProfile.Id;

        _ = SwitchToProfileById(importedProfile.Id);
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (_profileStore.Profiles.Count <= 1)
            return;

        int removeIndex = GetActiveProfileIndex();
        string removedId = _profile.Id;

        _profileStore.Profiles.RemoveAll(profile => string.Equals(profile.Id, removedId, StringComparison.Ordinal));
        if (_profileStore.Profiles.Count == 0)
        {
            var fallbackProfile = new DeckProfile { Name = "Standard" };
            fallbackProfile.Initialize();
            _profileStore.Profiles.Add(fallbackProfile);
        }

        int nextIndex = Math.Clamp(removeIndex, 0, _profileStore.Profiles.Count - 1);
        _ = SwitchToProfileById(_profileStore.Profiles[nextIndex].Id);
    }

    [RelayCommand]
    private void RenameProfile(string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return;

        string trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)
            || string.Equals(_profile.Name, trimmedName, StringComparison.Ordinal))
        {
            return;
        }

        _profile.Name = trimmedName;
        RebuildProfileOptions();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (IsViewingVirtualLayout)
            return;

        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            LoadCurrentLayout();
            NotifyPageChanged();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (IsViewingVirtualLayout)
            return;

        if (CurrentPageIndex < _profile.Pages.Count - 1)
        {
            CurrentPageIndex++;
            LoadCurrentLayout();
            NotifyPageChanged();
        }
    }

    [RelayCommand]
    private void IncreaseRows()
    {
        Rows += 1;
    }

    [RelayCommand]
    private void DecreaseRows()
    {
        Rows -= 1;
    }

    [RelayCommand]
    private void IncreaseColumns()
    {
        Columns += 1;
    }

    [RelayCommand]
    private void DecreaseColumns()
    {
        Columns -= 1;
    }

    [RelayCommand]
    private void AddPage()
    {
        var newPage = new DeckPage
        {
            Name = $"Page {_profile.Pages.Count + 1}",
            Rows = Rows,
            Columns = Columns
        };
        _profile.Pages.Add(newPage);
        SetVirtualLayoutIndex(-1);
        CurrentPageIndex = _profile.Pages.Count - 1;
        LoadCurrentLayout();
        RebuildLayoutTargets();
        NotifyPageChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (IsViewingVirtualLayout)
            return;

        if (_profile.Pages.Count <= 1) return;

        string removedId = CurrentRegularPage.Id;
        _profile.Pages.RemoveAt(CurrentPageIndex);
        ClearLayoutTargetReferences(removedId);

        if (CurrentPageIndex >= _profile.Pages.Count)
            CurrentPageIndex = _profile.Pages.Count - 1;

        LoadCurrentLayout();
        RebuildLayoutTargets();
        NotifyPageChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RenamePage(string? newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
        {
            CurrentLayout.Name = newName.Trim();
            OnPropertyChanged(nameof(CurrentPageName));
            RebuildLayoutTargets();
            ScheduleAutoSave();
        }
    }

    [RelayCommand]
    private void AddVirtualLayout()
    {
        var virtualLayout = new DeckPage
        {
            Name = $"Virtual {_profile.VirtualLayouts.Count + 1}",
            Rows = Rows,
            Columns = Columns
        };

        virtualLayout.EnsureButtonCount(Rows, Columns);
        _profile.VirtualLayouts.Add(virtualLayout);
        RebuildLayoutTargets();
        SwitchToLayoutById(virtualLayout.Id);
        NotifyPageChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveVirtualLayout()
    {
        if (!IsViewingVirtualLayout || _profile.VirtualLayouts.Count == 0)
            return;

        int removeIndex = _currentVirtualLayoutIndex;
        string removedId = _profile.VirtualLayouts[removeIndex].Id;

        _profile.VirtualLayouts.RemoveAt(removeIndex);
        ClearLayoutTargetReferences(removedId);

        if (_profile.VirtualLayouts.Count == 0)
        {
            SetVirtualLayoutIndex(-1);
            LoadCurrentLayout();
        }
        else
        {
            int nextIndex = Math.Min(removeIndex, _profile.VirtualLayouts.Count - 1);
            SetVirtualLayoutIndex(nextIndex);
            LoadCurrentLayout();
        }

        RebuildLayoutTargets();
        NotifyPageChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void ExitVirtualLayout()
    {
        if (!IsViewingVirtualLayout)
            return;

        SetVirtualLayoutIndex(-1);
        LoadCurrentLayout();
        NotifyPageChanged();
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(IsViewingVirtualLayout));
        OnPropertyChanged(nameof(CurrentLayoutKindLabel));
        OnPropertyChanged(nameof(HasVirtualLayouts));
        OnPropertyChanged(nameof(CanRemoveCurrentVirtualLayout));
        OnPropertyChanged(nameof(CurrentPageName));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(PageIndicator));
        OnPropertyChanged(nameof(HasMultiplePages));

        SyncSelectedLayoutId();
        NotifyLayoutChanged();
    }

    [RelayCommand]
    private void PreviousNotePage()
    {
        if (CanGoToPreviousNotePage)
            CurrentNotePageIndex--;
    }

    [RelayCommand]
    private void NextNotePage()
    {
        if (CanGoToNextNotePage)
            CurrentNotePageIndex++;
    }

    [RelayCommand]
    private void AddNotePage()
    {
        var notePage = new NotePage
        {
            Name = $"Notes {_profile.NotePages.Count + 1}"
        };
        notePage.EnsureInitialized();

        _profile.NotePages.Add(notePage);
        CurrentNotePageIndex = _profile.NotePages.Count - 1;
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveNotePage()
    {
        if (_profile.NotePages.Count <= 1)
            return;

        int removedIndex = CurrentNotePageIndex;
        _profile.NotePages.RemoveAt(removedIndex);

        if (CurrentNotePageIndex >= _profile.NotePages.Count)
        {
            CurrentNotePageIndex = _profile.NotePages.Count - 1;
        }
        else
        {
            _profile.CurrentNotePageIndex = CurrentNotePageIndex;
            LoadStickyNotes();
            NotifyNotePageChanged();
        }

        ScheduleAutoSave();
    }

    private void NotifyNotePageChanged()
    {
        OnPropertyChanged(nameof(CurrentNotePageName));
        OnPropertyChanged(nameof(NotePageCount));
        OnPropertyChanged(nameof(CanGoToPreviousNotePage));
        OnPropertyChanged(nameof(CanGoToNextNotePage));
        OnPropertyChanged(nameof(CanRemoveNotePage));
        OnPropertyChanged(nameof(NotePageIndicator));
        OnPropertyChanged(nameof(HasMultipleNotePages));
        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
    }

    [RelayCommand]
    private void ToggleStickyNotes()
    {
        StickyNotesVisible = true;
    }

    [RelayCommand]
    private void AddStickyNote()
    {
        int offset = StickyNotes.Count * 20;
        var note = new StickyNote
        {
            Title = $"Sticky note {StickyNotes.Count + 1}",
            Text = string.Empty,
            X = 96 + offset,
            Y = 140 + offset,
            Width = 230,
            Height = 180,
            Color = "#F8E784"
        };

        CurrentNotePage.StickyNotes.Add(note);
        StickyNotes.Add(new StickyNoteViewModel(note, ScheduleAutoSave));

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveStickyNote(StickyNoteViewModel? note)
    {
        if (note == null)
            return;

        CurrentNotePage.StickyNotes.Remove(note.Model);
        StickyNotes.Remove(note);

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void AddQuickTextItem()
    {
        string categoryId = string.IsNullOrWhiteSpace(SelectedQuickTextCategoryId)
            ? _profile.ActiveQuickTextCategoryId
            : SelectedQuickTextCategoryId;

        var item = new QuickTextItem
        {
            Text = string.Empty,
            CategoryId = categoryId
        };
        item.EnsureInitialized();

        _profile.QuickTextItems.Add(item);

        LoadQuickTextItemsForSelectedCategory();

        OnPropertyChanged(nameof(HasQuickTextItems));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveQuickTextItem(QuickTextItemViewModel? item)
    {
        if (item == null)
            return;

        _profile.QuickTextItems.Remove(item.Model);
        QuickTextItems.Remove(item);

        OnPropertyChanged(nameof(HasQuickTextItems));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void DuplicateQuickTextItem(QuickTextItemViewModel? source)
    {
        if (source == null)
            return;

        var duplicate = new QuickTextItem
        {
            Text = source.Text,
            CategoryId = source.Model.CategoryId
        };
        duplicate.EnsureInitialized();

        _profile.QuickTextItems.Add(duplicate);

        LoadQuickTextItemsForSelectedCategory();

        OnPropertyChanged(nameof(HasQuickTextItems));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void ClearQuickTextSearch()
    {
        if (string.IsNullOrWhiteSpace(QuickTextSearchQuery))
            return;

        QuickTextSearchQuery = string.Empty;
    }

    [RelayCommand]
    private void AddQuickTextCategory()
    {
        var category = new QuickTextCategory
        {
            Name = CreateUniqueQuickTextCategoryName($"Category {_profile.QuickTextCategories.Count + 1}")
        };
        category.EnsureInitialized();

        _profile.QuickTextCategories.Add(category);
        QuickTextCategories.Add(category);
        SelectedQuickTextCategoryId = category.Id;

        NotifyQuickTextCategoryChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveQuickTextCategory()
    {
        if (_profile.QuickTextCategories.Count <= 1)
            return;

        string removeId = string.IsNullOrWhiteSpace(SelectedQuickTextCategoryId)
            ? _profile.ActiveQuickTextCategoryId
            : SelectedQuickTextCategoryId;

        int removeIndex = _profile.QuickTextCategories.FindIndex(category => string.Equals(category.Id, removeId, StringComparison.Ordinal));
        if (removeIndex < 0)
            return;

        _profile.QuickTextCategories.RemoveAt(removeIndex);
        QuickTextCategories.RemoveAt(removeIndex);

        int targetIndex = Math.Clamp(removeIndex, 0, _profile.QuickTextCategories.Count - 1);
        string targetCategoryId = _profile.QuickTextCategories[targetIndex].Id;

        foreach (var item in _profile.QuickTextItems)
        {
            if (string.Equals(item.CategoryId, removeId, StringComparison.Ordinal))
                item.CategoryId = targetCategoryId;
        }

        SelectedQuickTextCategoryId = targetCategoryId;

        NotifyQuickTextCategoryChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RenameQuickTextCategory(string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return;

        string normalized = newName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var current = CurrentQuickTextCategory;
        if (current == null)
            return;

        current.Name = normalized;
        LoadQuickTextCategories();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void PreviousQuickTextCategory()
    {
        if (!CanGoToPreviousQuickTextCategory)
            return;

        SelectedQuickTextCategoryId = QuickTextCategories[CurrentQuickTextCategoryIndex - 1].Id;
    }

    [RelayCommand]
    private void NextQuickTextCategory()
    {
        if (!CanGoToNextQuickTextCategory)
            return;

        SelectedQuickTextCategoryId = QuickTextCategories[CurrentQuickTextCategoryIndex + 1].Id;
    }

    [RelayCommand]
    private void SetQuickTextCategory(QuickTextCategory? category)
    {
        if (category == null || string.IsNullOrWhiteSpace(category.Id))
            return;

        if (string.Equals(SelectedQuickTextCategoryId, category.Id, StringComparison.Ordinal))
            return;

        SelectedQuickTextCategoryId = category.Id;
    }

    [RelayCommand]
    private void ShowButtonEditor()
    {
        IsClipboardEditorMode = false;
    }

    [RelayCommand]
    private void ShowClipboardEditor()
    {
        IsClipboardEditorMode = true;
    }

    [RelayCommand]
    private void FollowNavigationTarget(ButtonViewModel? sourceButton)
    {
        if (sourceButton == null
            || sourceButton.ActionType != ActionType.LayoutNavigation
            || string.IsNullOrWhiteSpace(sourceButton.TargetLayoutId))
        {
            return;
        }

        _ = SwitchToLayoutById(sourceButton.TargetLayoutId, sourceButton.Index);
    }

    public void SetStickyNoteColor(StickyNoteViewModel? note, string color)
    {
        if (note == null || string.IsNullOrWhiteSpace(color))
            return;

        note.Color = color;
    }

    private async Task RefreshOverlayBackgroundImageAsync()
    {
        string pathSnapshot = _profile.OverlayBackgroundImagePath;
        if (string.IsNullOrWhiteSpace(pathSnapshot))
        {
            OverlayBackgroundImageVersion++;
            return;
        }

        bool loaded = await OverlayImageCache.EnsureLoadedAsync(pathSnapshot);
        if (!loaded)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (string.Equals(pathSnapshot, _profile.OverlayBackgroundImagePath, StringComparison.OrdinalIgnoreCase))
                    OverlayBackgroundImageVersion++;
            });
            return;
        }

        if (string.Equals(pathSnapshot, _profile.OverlayBackgroundImagePath, StringComparison.OrdinalIgnoreCase))
            OverlayBackgroundImageVersion++;
    }

    private int FindNextEmptyButtonIndex(int sourceIndex)
    {
        int total = CurrentLayout.Buttons.Count;
        for (int offset = 1; offset < total; offset++)
        {
            int idx = (sourceIndex + offset) % total;
            if (IsButtonSlotEmpty(CurrentLayout.Buttons[idx]))
                return idx;
        }

        return -1;
    }

    private static bool IsButtonSlotEmpty(ButtonConfig config)
    {
        return config.ActionType == ActionType.None
            && string.IsNullOrWhiteSpace(config.Title)
            && string.IsNullOrWhiteSpace(config.IconText)
            && string.IsNullOrWhiteSpace(config.ImagePath)
            && string.IsNullOrWhiteSpace(config.Text)
            && string.IsNullOrWhiteSpace(config.KeyText)
            && string.IsNullOrWhiteSpace(config.TargetLayoutId)
            && config.Shape == ButtonShape.None
            && !config.PressEnterAfter
            && config.Steps.Count == 0;
    }

    private static ButtonConfig CloneButtonConfig(ButtonConfig source)
    {
        var clone = new ButtonConfig
        {
            Title = source.Title,
            ActionType = source.ActionType,
            BackgroundColor = source.BackgroundColor,
            TextColor = source.TextColor,
            IconText = source.IconText,
            CornerRadius = source.CornerRadius,
            ImagePath = source.ImagePath,
            Text = source.Text,
            PressEnterAfter = source.PressEnterAfter,
            TextMode = source.TextMode,
            KeyText = source.KeyText,
            TargetLayoutId = source.TargetLayoutId,
            Shape = source.Shape
        };

        foreach (var step in source.Steps)
        {
            clone.Steps.Add(new ActionStep
            {
                Type = step.Type,
                KeyText = step.KeyText,
                Text = step.Text,
                TextMode = step.TextMode,
                PressEnterAfter = step.PressEnterAfter,
                DelayMs = step.DelayMs
            });
        }

        return clone;
    }

    private static DeckProfile CloneProfile(DeckProfile source)
    {
        string json = JsonSerializer.Serialize(source, ProfileCloneJsonOptions);
        return JsonSerializer.Deserialize<DeckProfile>(json, ProfileCloneJsonOptions) ?? new DeckProfile();
    }

    [RelayCommand]
    private void NewButton()
    {
        if (SelectedButton == null) return;
        SelectedButton.ActionType = ActionType.TextInput;
        SelectedButton.Title = "New Action";
    }

    [RelayCommand]
    private void DuplicateButton()
    {
        if (SelectedButton == null) return;

        int sourceIndex = SelectedButton.Index;
        int targetIndex = FindNextEmptyButtonIndex(sourceIndex);
        if (targetIndex < 0) return;

        CurrentLayout.Buttons[targetIndex] = CloneButtonConfig(SelectedButton.Config);
        LoadCurrentLayout(targetIndex);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void CopyButton(ButtonViewModel? source)
    {
        var sourceButton = source ?? SelectedButton;
        if (sourceButton == null) return;

        _buttonClipboard = CloneButtonConfig(sourceButton.Config);
    }

    [RelayCommand]
    private void PasteButton(ButtonViewModel? target)
    {
        if (_buttonClipboard == null || target == null)
            return;

        CurrentLayout.Buttons[target.Index] = CloneButtonConfig(_buttonClipboard);
        LoadCurrentLayout(target.Index);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void ClearButton()
    {
        if (SelectedButton == null) return;
        SelectedButton.ActionType = ActionType.None;
        SelectedButton.Title = string.Empty;
        SelectedButton.Text = string.Empty;
        SelectedButton.PressEnterAfter = false;
        SelectedButton.TextMode = TextMode.PasteFromClipboard;
        SelectedButton.IconText = string.Empty;
        SelectedButton.ImagePath = string.Empty;
        SelectedButton.KeyText = string.Empty;
        SelectedButton.TargetLayoutId = string.Empty;
        SelectedButton.Shape = Models.ButtonShape.None;
        SelectedButton.Steps.Clear();
        OnPropertyChanged(nameof(SelectedButton));
    }

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedButton == null) return;
        SelectedButton.Steps.Add(new Models.ActionStep());
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveStep(Models.ActionStep? step)
    {
        if (SelectedButton == null || step == null) return;
        SelectedButton.Steps.Remove(step);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void MoveStepUp(Models.ActionStep? step)
    {
        if (SelectedButton == null || step == null) return;
        int index = SelectedButton.Steps.IndexOf(step);
        if (index > 0)
        {
            SelectedButton.Steps.Move(index, index - 1);
            ScheduleAutoSave();
        }
    }

    [RelayCommand]
    private void MoveStepDown(Models.ActionStep? step)
    {
        if (SelectedButton == null || step == null) return;
        int index = SelectedButton.Steps.IndexOf(step);
        if (index >= 0 && index < SelectedButton.Steps.Count - 1)
        {
            SelectedButton.Steps.Move(index, index + 1);
            ScheduleAutoSave();
        }
    }

    // ---- Clipboard action step commands ----

    private void LoadQuickTextActionSteps()
    {
        QuickTextActionSteps.CollectionChanged -= OnQuickTextActionStepsCollectionChanged;

        foreach (var step in QuickTextActionSteps)
            step.PropertyChanged -= OnClipboardActionStepPropertyChanged;

        QuickTextActionSteps = new ObservableCollection<ActionStep>(_profile.QuickTextActionSteps);
        QuickTextActionSteps.CollectionChanged += OnQuickTextActionStepsCollectionChanged;

        foreach (var step in QuickTextActionSteps)
            step.PropertyChanged += OnClipboardActionStepPropertyChanged;

        OnPropertyChanged(nameof(QuickTextActionSteps));
        OnPropertyChanged(nameof(HasQuickTextAction));
    }

    private void OnQuickTextActionStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep model list in sync
        _profile.QuickTextActionSteps.Clear();
        foreach (var step in QuickTextActionSteps)
            _profile.QuickTextActionSteps.Add(step);

        if (e.OldItems != null)
            foreach (var item in e.OldItems.OfType<ActionStep>())
                item.PropertyChanged -= OnClipboardActionStepPropertyChanged;

        if (e.NewItems != null)
            foreach (var item in e.NewItems.OfType<ActionStep>())
                item.PropertyChanged += OnClipboardActionStepPropertyChanged;

        OnPropertyChanged(nameof(HasQuickTextAction));
        ScheduleAutoSave();
    }

    private void OnClipboardActionStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep model list in sync (steps are shared references, so property changes are automatic)
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void AddClipboardActionStep()
    {
        QuickTextActionSteps.Add(new Models.ActionStep());
    }

    [RelayCommand]
    private void RemoveClipboardActionStep(Models.ActionStep? step)
    {
        if (step == null)
            return;

        QuickTextActionSteps.Remove(step);
    }

    [RelayCommand]
    private void MoveClipboardActionStepUp(Models.ActionStep? step)
    {
        if (step == null)
            return;

        int index = QuickTextActionSteps.IndexOf(step);
        if (index > 0)
            QuickTextActionSteps.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveClipboardActionStepDown(Models.ActionStep? step)
    {
        if (step == null)
            return;

        int index = QuickTextActionSteps.IndexOf(step);
        if (index >= 0 && index < QuickTextActionSteps.Count - 1)
            QuickTextActionSteps.Move(index, index + 1);
    }

    public void ExecuteQuickTextAction(string itemText)
    {
        if (string.IsNullOrWhiteSpace(itemText) || _profile.QuickTextActionSteps.Count == 0)
            return;

        _multiActionService.ExecuteWithItemText(_profile.QuickTextActionSteps, itemText, NaturalTypingEnabled);
    }

    private bool SwitchToLayoutById(string layoutId, int? preferredSelectedIndex = null)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
            return false;

        if (string.Equals(CurrentLayout.Id, layoutId, StringComparison.Ordinal) && preferredSelectedIndex == null)
            return true;

        int regularIndex = _profile.Pages.FindIndex(page => string.Equals(page.Id, layoutId, StringComparison.Ordinal));
        if (regularIndex >= 0)
        {
            SetVirtualLayoutIndex(-1);
            CurrentPageIndex = regularIndex;
            LoadCurrentLayout(preferredSelectedIndex);
            NotifyPageChanged();
            return true;
        }

        int virtualIndex = _profile.VirtualLayouts.FindIndex(page => string.Equals(page.Id, layoutId, StringComparison.Ordinal));
        if (virtualIndex >= 0)
        {
            SetVirtualLayoutIndex(virtualIndex);
            LoadCurrentLayout(preferredSelectedIndex);
            NotifyPageChanged();
            return true;
        }

        return false;
    }

    private bool SwitchToProfileById(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return false;

        if (string.Equals(_profile.Id, profileId, StringComparison.Ordinal))
            return true;

        var targetProfile = _profileStore.Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));
        if (targetProfile == null)
            return false;

        _profile = targetProfile;
        _profile.Initialize();
        _profileStore.ActiveProfileId = _profile.Id;

        SetVirtualLayoutIndex(-1);
        CurrentPageIndex = 0;
        CurrentNotePageIndex = Math.Clamp(_profile.CurrentNotePageIndex, 0, _profile.NotePages.Count - 1);
        StickyNotesVisible = true;

        RebuildLayoutTargets();
        LoadCurrentLayout();
        LoadQuickTextCategories();
        LoadQuickTextActionSteps();
        NotifyPageChanged();
        NotifyNotePageChanged();

        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(OverlayBackgroundColor));
        OnPropertyChanged(nameof(OverlayBackgroundImagePath));
        OnPropertyChanged(nameof(ButtonOverlayOpacity));
        OnPropertyChanged(nameof(ButtonSpacing));
        OnPropertyChanged(nameof(ButtonSize));
        OnPropertyChanged(nameof(StickyNoteFontSize));
        OnPropertyChanged(nameof(QuickTextSearchQuery));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        OnPropertyChanged(nameof(GridOffsetX));
        OnPropertyChanged(nameof(GridOffsetY));
        OnPropertyChanged(nameof(QuickTextPanelX));
        OnPropertyChanged(nameof(QuickTextPanelY));
        OnPropertyChanged(nameof(QuickTextPanelWidth));
        OnPropertyChanged(nameof(QuickTextPanelHeight));
        OnPropertyChanged(nameof(QuickTextFontSize));
        OnPropertyChanged(nameof(QuickTextPreviewLineHeight));
        OnPropertyChanged(nameof(QuickTextPreviewHeight));
        OnPropertyChanged(nameof(QuickTextEditorHeight));
        OnPropertyChanged(nameof(QuickTextHintLineHeight));
        OnPropertyChanged(nameof(QuickTextHintMaxHeight));
        OnPropertyChanged(nameof(HasQuickTextAction));
        OnPropertyChanged(nameof(HotkeyModifiers));
        OnPropertyChanged(nameof(HotkeyVk));
        OnPropertyChanged(nameof(HotkeyDisplayText));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(NaturalTypingEnabled));
        OnPropertyChanged(nameof(GamepadSupportEnabled));
        OnPropertyChanged(nameof(GamepadToggleButtons));
        OnPropertyChanged(nameof(GamepadToggleDisplayText));

        RebuildProfileOptions();
        SyncSelectedProfileId();
        ScheduleAutoSave();
        return true;
    }

    private void SetVirtualLayoutIndex(int value)
    {
        if (_currentVirtualLayoutIndex == value)
            return;

        _currentVirtualLayoutIndex = value;
        OnPropertyChanged(nameof(IsViewingVirtualLayout));
    }

    private void RebuildLayoutTargets()
    {
        LayoutTargets.Clear();

        for (int i = 0; i < _profile.Pages.Count; i++)
        {
            var page = _profile.Pages[i];
            LayoutTargets.Add(new LayoutTargetOption
            {
                Id = page.Id,
                Label = $"Page {i + 1}: {page.Name}"
            });
        }

        for (int i = 0; i < _profile.VirtualLayouts.Count; i++)
        {
            var layout = _profile.VirtualLayouts[i];
            LayoutTargets.Add(new LayoutTargetOption
            {
                Id = layout.Id,
                Label = $"Virtual {i + 1}: {layout.Name}"
            });
        }

        OnPropertyChanged(nameof(HasVirtualLayouts));
        SyncSelectedLayoutId();
    }

    private void RebuildProfileOptions()
    {
        ProfileOptions.Clear();

        for (int i = 0; i < _profileStore.Profiles.Count; i++)
        {
            var profile = _profileStore.Profiles[i];
            ProfileOptions.Add(new ProfileOption
            {
                Id = profile.Id,
                Label = profile.Name
            });
        }

        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(ProfileCount));
        OnPropertyChanged(nameof(CanRemoveProfile));
        OnPropertyChanged(nameof(ProfileIndicator));

        SyncSelectedProfileId();
    }

    private void SyncSelectedProfileId()
    {
        string currentId = _profile.Id;
        if (!string.Equals(SelectedProfileId, currentId, StringComparison.Ordinal))
            SelectedProfileId = currentId;
    }

    private void SyncSelectedLayoutId()
    {
        string currentId = CurrentLayout.Id;
        if (!string.Equals(SelectedLayoutId, currentId, StringComparison.Ordinal))
            SelectedLayoutId = currentId;
    }

    private void ClearLayoutTargetReferences(string removedLayoutId)
    {
        if (string.IsNullOrWhiteSpace(removedLayoutId))
            return;

        foreach (var layout in _profile.Pages)
            ClearLayoutTargetReferences(layout, removedLayoutId);

        foreach (var layout in _profile.VirtualLayouts)
            ClearLayoutTargetReferences(layout, removedLayoutId);
    }

    private static void ClearLayoutTargetReferences(DeckPage layout, string removedLayoutId)
    {
        foreach (var button in layout.Buttons)
        {
            if (string.Equals(button.TargetLayoutId, removedLayoutId, StringComparison.Ordinal))
                button.TargetLayoutId = string.Empty;
        }
    }

    public void Dispose()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Dispose();
        DetachButtonHandlers();
    }
}
