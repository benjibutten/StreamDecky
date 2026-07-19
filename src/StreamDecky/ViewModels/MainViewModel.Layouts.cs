using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<LayoutTargetOption> LayoutTargets { get; } = new();

    [ObservableProperty]
    private int _currentPageIndex;

    [ObservableProperty]
    private string? _selectedLayoutId;

    partial void OnSelectedLayoutIdChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SwitchToLayoutById(value);
    }

    public bool IsViewingVirtualLayout => _currentVirtualLayoutIndex >= 0;
    public bool HasVirtualLayouts => _profile.VirtualLayouts.Count > 0;
    public string CurrentLayoutKindLabel => IsViewingVirtualLayout ? "Virtual Layout" : "Page";
    public bool CanRemoveCurrentVirtualLayout => IsViewingVirtualLayout && _profile.VirtualLayouts.Count > 0;

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

        if (_profile.Pages.Count <= 1)
            return;

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