using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;
using StreamDecky.Models;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileService _profileService = new();
    private readonly TextInputActionService _textInputService = new();
    private readonly MultiActionService _multiActionService = new();
    private readonly System.Timers.Timer _autoSaveTimer;

    private DeckProfile _profile;
    private ButtonConfig? _buttonClipboard;
    private int _currentVirtualLayoutIndex = -1;

    public ObservableCollection<LayoutTargetOption> LayoutTargets { get; } = new();

    public MainViewModel()
    {
        _profile = _profileService.Load();
        _currentPageIndex = 0;
        _currentNotePageIndex = Math.Clamp(_profile.CurrentNotePageIndex, 0, _profile.NotePages.Count - 1);

        LoadCurrentLayout();
        StickyNotesVisible = _profile.StickyNotesVisible;

        // Auto-save: debounce 1 second after last change
        _autoSaveTimer = new System.Timers.Timer(1000) { AutoReset = false };
        _autoSaveTimer.Elapsed += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _profileService.Save(_profile);
            });
        };

        // Listen for any property change to trigger auto-save
        PropertyChanged += (_, _) => ScheduleAutoSave();

        RebuildLayoutTargets();
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
    private bool _stickyNotesVisible;

    [ObservableProperty]
    private string? _selectedLayoutId;

    partial void OnStickyNotesVisibleChanged(bool value)
    {
        _profile.StickyNotesVisible = value;
    }

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
    }

    partial void OnSelectedLayoutIdChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SwitchToLayoutById(value);
    }

    public bool IsButtonSelected => SelectedButton != null;
    public bool IsViewingVirtualLayout => _currentVirtualLayoutIndex >= 0;
    public bool HasVirtualLayouts => _profile.VirtualLayouts.Count > 0;
    public string CurrentLayoutKindLabel => IsViewingVirtualLayout ? "Virtual Layout" : "Page";
    public bool CanRemoveCurrentVirtualLayout => IsViewingVirtualLayout && _profile.VirtualLayouts.Count > 0;

    public DeckProfile Profile => _profile;

    public string OverlayBackgroundColor
    {
        get => _profile.OverlayBackgroundColor;
        set
        {
            _profile.OverlayBackgroundColor = value;
            OnPropertyChanged();
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
        }
    }

    public double ButtonSpacing
    {
        get => _profile.ButtonSpacing;
        set
        {
            _profile.ButtonSpacing = value;
            OnPropertyChanged();
        }
    }

    public double ButtonSize
    {
        get => _profile.ButtonSize;
        set
        {
            _profile.ButtonSize = value;
            OnPropertyChanged();
        }
    }

    public double GridOffsetX
    {
        get => _profile.GridOffsetX;
        set
        {
            _profile.GridOffsetX = value;
            OnPropertyChanged();
        }
    }

    public double GridOffsetY
    {
        get => _profile.GridOffsetY;
        set
        {
            _profile.GridOffsetY = value;
            OnPropertyChanged();
        }
    }

    public uint HotkeyModifiers
    {
        get => _profile.HotkeyModifiers;
        set
        {
            _profile.HotkeyModifiers = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplayText));
        }
    }

    public uint HotkeyVk
    {
        get => _profile.HotkeyVk;
        set
        {
            _profile.HotkeyVk = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplayText));
        }
    }

    public string HotkeyDisplayText
    {
        get => _profile.HotkeyDisplayText;
        set
        {
            _profile.HotkeyDisplayText = value;
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _profile.StartWithWindows;
        set
        {
            _profile.StartWithWindows = value;
            OnPropertyChanged();
        }
    }

    public bool NaturalTypingEnabled
    {
        get => _profile.NaturalTypingEnabled;
        set
        {
            _profile.NaturalTypingEnabled = value;
            OnPropertyChanged();
        }
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

    private void LoadCurrentLayout(int? preferredSelectedIndex = null)
    {
        int? selectedIndex = preferredSelectedIndex ?? SelectedButton?.Index;

        CurrentLayout.EnsureButtonCount(Rows, Columns);
        var buttonViewModels = new List<ButtonViewModel>(CurrentLayout.Buttons.Count);
        for (int i = 0; i < CurrentLayout.Buttons.Count; i++)
        {
            var bvm = new ButtonViewModel(CurrentLayout.Buttons[i], i);
            bvm.PropertyChanged += (_, e) =>
            {
                ScheduleAutoSave();

                if (e.PropertyName is nameof(ButtonViewModel.IsConfigured)
                    or nameof(ButtonViewModel.ActionType)
                    or nameof(ButtonViewModel.Title)
                    or nameof(ButtonViewModel.IconText)
                    or nameof(ButtonViewModel.ImagePath)
                    or null)
                {
                    ButtonVisualVersion++;
                }
            };
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

    private void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(ButtonSlots));
        OnPropertyChanged(nameof(LayoutSummary));
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
        _profileService.Save(_profile);
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
    }

    [RelayCommand]
    private void RenamePage(string? newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
        {
            CurrentLayout.Name = newName.Trim();
            OnPropertyChanged(nameof(CurrentPageName));
            RebuildLayoutTargets();
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
        StickyNotesVisible = !StickyNotesVisible;
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

        StickyNotesVisible = true;
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

        if (StickyNotes.Count == 0)
            StickyNotesVisible = false;

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
        ScheduleAutoSave();
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
}
