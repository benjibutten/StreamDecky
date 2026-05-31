using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private ObservableCollection<QuickTextItemViewModel> _quickTextItems = new();

    [ObservableProperty]
    private ObservableCollection<QuickTextCategory> _quickTextCategories = new();

    [ObservableProperty]
    private string _quickTextSearchQuery = string.Empty;

    [ObservableProperty]
    private string? _selectedQuickTextCategoryId;

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

    public bool HasQuickTextItems => QuickTextItems.Count > 0;
    public bool HasAnyQuickTextItems => _profile.QuickTextItems.Count > 0;
    public int QuickTextCategoryCount => QuickTextCategories.Count;
    public bool HasMultipleQuickTextCategories => QuickTextCategoryCount > 1;
    public string CurrentQuickTextCategoryName => CurrentQuickTextCategory?.Name ?? "General";
    public bool CanGoToPreviousQuickTextCategory => CurrentQuickTextCategoryIndex > 0;
    public bool CanGoToNextQuickTextCategory => CurrentQuickTextCategoryIndex < QuickTextCategoryCount - 1;

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

    private int CurrentQuickTextCategoryIndex => QuickTextCategories
        .Select((category, index) => new { category, index })
        .FirstOrDefault(entry => string.Equals(entry.category.Id, SelectedQuickTextCategoryId, StringComparison.Ordinal))?.index ?? 0;

    private QuickTextCategory? CurrentQuickTextCategory => QuickTextCategories.Count == 0
        ? null
        : QuickTextCategories[Math.Clamp(CurrentQuickTextCategoryIndex, 0, QuickTextCategories.Count - 1)];

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
}