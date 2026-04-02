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
    private DeckProfile _profile;
    private readonly System.Timers.Timer _autoSaveTimer;

    public MainViewModel()
    {
        _profile = _profileService.Load();
        LoadCurrentPage();

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
    private bool _isOverlayOpen;

    [ObservableProperty]
    private int _buttonVisualVersion;

    [ObservableProperty]
    private int _overlayBackgroundImageVersion;

    [ObservableProperty]
    private ObservableCollection<StickyNoteViewModel> _stickyNotes = new();

    [ObservableProperty]
    private bool _stickyNotesVisible;

    public bool IsButtonSelected => SelectedButton != null;

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

    public int Rows
    {
        get => CurrentPage.Rows;
        set => UpdateCurrentPageLayout(value, Columns);
    }

    public int Columns
    {
        get => CurrentPage.Columns;
        set => UpdateCurrentPageLayout(Rows, value);
    }

    public int MinRows => DeckPage.MinRows;
    public int MaxRows => DeckPage.MaxRows;
    public int MinColumns => DeckPage.MinColumns;
    public int MaxColumns => DeckPage.MaxColumns;
    public int MaxButtonsPerPage => DeckPage.MaxButtonsPerPage;
    public int ButtonSlots => Rows * Columns;
    public string LayoutSummary => $"{Rows} x {Columns} ({ButtonSlots} slots)";

    public string CurrentPageName => CurrentPage.Name;
    public int PageCount => _profile.Pages.Count;
    public bool CanGoToPreviousPage => CurrentPageIndex > 0;
    public bool CanGoToNextPage => CurrentPageIndex < _profile.Pages.Count - 1;
    public string PageIndicator => $"{CurrentPageIndex + 1} / {PageCount}";
    public bool HasMultiplePages => _profile.Pages.Count > 1;
    public bool HasStickyNotes => StickyNotes.Count > 0;

    private DeckPage CurrentPage => _profile.Pages[CurrentPageIndex];

    private void LoadCurrentPage(int? preferredSelectedIndex = null)
    {
        int? selectedIndex = preferredSelectedIndex ?? SelectedButton?.Index;

        CurrentPage.EnsureButtonCount();
        Buttons.Clear();
        for (int i = 0; i < CurrentPage.Buttons.Count; i++)
        {
            var bvm = new ButtonViewModel(CurrentPage.Buttons[i], i);
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
            Buttons.Add(bvm);
        }

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
    }

    private void LoadStickyNotes()
    {
        StickyNotes.Clear();
        foreach (var note in CurrentPage.StickyNotes)
        {
            StickyNotes.Add(new StickyNoteViewModel(note, ScheduleAutoSave));
        }

        OnPropertyChanged(nameof(HasStickyNotes));
    }

    private void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(ButtonSlots));
        OnPropertyChanged(nameof(LayoutSummary));
    }

    private void UpdateCurrentPageLayout(int rows, int columns)
    {
        rows = Math.Clamp(rows, MinRows, MaxRows);
        columns = Math.Clamp(columns, MinColumns, MaxColumns);

        if (CurrentPage.Rows == rows && CurrentPage.Columns == columns)
            return;

        int? selectedIndex = SelectedButton?.Index;
        CurrentPage.Rows = rows;
        CurrentPage.Columns = columns;
        LoadCurrentPage(selectedIndex);
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
        StickyNotesVisible = false;
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

        // Close overlay first so the target window gets focus
        IsOverlayOpen = false;

        switch (button.ActionType)
        {
            case ActionType.TextInput:
                _textInputService.Execute(button.Config);
                break;
            case ActionType.KeyPress:
                _multiActionService.ExecuteKeyPress(button.Config.KeyText);
                break;
            case ActionType.MultiAction:
                await _multiActionService.ExecuteAsync(button.Config);
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
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            LoadCurrentPage();
            NotifyPageChanged();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPageIndex < _profile.Pages.Count - 1)
        {
            CurrentPageIndex++;
            LoadCurrentPage();
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
            Rows = CurrentPage.Rows,
            Columns = CurrentPage.Columns
        };
        _profile.Pages.Add(newPage);
        CurrentPageIndex = _profile.Pages.Count - 1;
        LoadCurrentPage();
        NotifyPageChanged();
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (_profile.Pages.Count <= 1) return;
        _profile.Pages.RemoveAt(CurrentPageIndex);
        if (CurrentPageIndex >= _profile.Pages.Count)
            CurrentPageIndex = _profile.Pages.Count - 1;
        LoadCurrentPage();
        NotifyPageChanged();
    }

    [RelayCommand]
    private void RenamePage(string? newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
        {
            CurrentPage.Name = newName;
            OnPropertyChanged(nameof(CurrentPageName));
        }
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(CurrentPageName));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(PageIndicator));
        OnPropertyChanged(nameof(HasMultiplePages));
        NotifyLayoutChanged();
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
            Text = string.Empty,
            X = 96 + offset,
            Y = 140 + offset,
            Width = 230,
            Height = 180,
            Color = "#F8E784"
        };

        CurrentPage.StickyNotes.Add(note);
        StickyNotes.Add(new StickyNoteViewModel(note, ScheduleAutoSave));

        StickyNotesVisible = true;
        OnPropertyChanged(nameof(HasStickyNotes));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveStickyNote(StickyNoteViewModel? note)
    {
        if (note == null)
            return;

        CurrentPage.StickyNotes.Remove(note.Model);
        StickyNotes.Remove(note);

        if (StickyNotes.Count == 0)
            StickyNotesVisible = false;

        OnPropertyChanged(nameof(HasStickyNotes));
        ScheduleAutoSave();
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

    [RelayCommand]
    private void NewButton()
    {
        if (SelectedButton == null) return;
        SelectedButton.ActionType = ActionType.TextInput;
        SelectedButton.Title = "New Action";
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
}
