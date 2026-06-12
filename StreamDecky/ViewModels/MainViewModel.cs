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
    private readonly ProfileService _profileService;
    private readonly TextInputActionService _textInputService;
    private readonly MultiActionService _multiActionService;
    private readonly List<ButtonViewModel> _trackedButtons = new();

    private DeckProfileStore _profileStore;
    private DeckProfile _profile;
    private ButtonConfig? _buttonClipboard;
    private int _currentVirtualLayoutIndex = -1;

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

    [ObservableProperty]
    private ObservableCollection<ButtonViewModel> _buttons = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsButtonSelected))]
    private ButtonViewModel? _selectedButton;

    [ObservableProperty]
    private bool _isOverlayOpen;

    [ObservableProperty]
    private int _buttonVisualVersion;

    [ObservableProperty]
    private int _overlayBackgroundImageVersion;

    [ObservableProperty]
    private bool _isClipboardEditorMode;

    partial void OnIsClipboardEditorModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsButtonEditorMode));
    }

    public bool IsButtonSelected => SelectedButton != null;
    public bool IsButtonEditorMode => !IsClipboardEditorMode;

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

    public void Dispose()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Dispose();
        DetachButtonHandlers();
    }
}
