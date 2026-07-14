using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private ObservableCollection<QuickTextItemViewModel> _quickTextItems = new();

    [ObservableProperty]
    private ObservableCollection<QuickTextCollection> _quickTextCollections = new();

    [ObservableProperty]
    private ObservableCollection<QuickTextCategory> _quickTextCategories = new();

    [ObservableProperty]
    private ObservableCollection<QuickTextCategory> _overlayQuickTextCategories = new();

    [ObservableProperty]
    private string? _selectedQuickTextCollectionId;

    [ObservableProperty]
    private string _quickTextSearchQuery = string.Empty;

    [ObservableProperty]
    private string? _selectedQuickTextCategoryId;

    partial void OnSelectedQuickTextCollectionIdChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!_profile.QuickTextCollections.Any(collection => string.Equals(collection.Id, value, StringComparison.Ordinal)))
        {
            if (QuickTextCollections.Count > 0)
                SelectedQuickTextCollectionId = QuickTextCollections[0].Id;
            return;
        }

        if (!string.Equals(_profile.ActiveQuickTextCollectionId, value, StringComparison.Ordinal))
        {
            _profile.ActiveQuickTextCollectionId = value;
            ScheduleAutoSave();
        }

        RebuildOverlayQuickTextCategories();
        if (OverlayQuickTextCategories.Count > 0
            && !OverlayQuickTextCategories.Any(tag => string.Equals(tag.Id, SelectedQuickTextCategoryId, StringComparison.Ordinal)))
        {
            SelectedQuickTextCategoryId = OverlayQuickTextCategories[0].Id;
        }
        else
        {
            LoadQuickTextItemsForSelectedCategory();
        }
        NotifyQuickTextCollectionChanged();
    }

    partial void OnSelectedQuickTextCategoryIdChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!QuickTextCategories.Any(category => string.Equals(category.Id, value, StringComparison.Ordinal)))
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
    public int QuickTextCollectionCount => QuickTextCollections.Count;
    public bool HasMultipleQuickTextCollections => QuickTextCollectionCount > 1;
    public string CurrentQuickTextCollectionName => CurrentQuickTextCollection?.Name ?? "General";
    public int QuickTextCategoryCount => QuickTextCategories.Count;
    public bool HasMultipleQuickTextCategories => QuickTextCategoryCount > 1;
    public string CurrentQuickTextCategoryName => CurrentQuickTextCategory?.Name ?? "General";

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

    private QuickTextCollection? CurrentQuickTextCollection => QuickTextCollections
        .FirstOrDefault(collection => string.Equals(collection.Id, SelectedQuickTextCollectionId, StringComparison.Ordinal));

    private QuickTextCategory? CurrentQuickTextCategory => QuickTextCategories
        .FirstOrDefault(category => string.Equals(category.Id, SelectedQuickTextCategoryId, StringComparison.Ordinal));

    private void LoadQuickTextCollections()
    {
        foreach (var collection in _profile.QuickTextCollections)
            collection.EnsureInitialized();

        if (_profile.QuickTextCollections.Count == 0)
        {
            var fallback = new QuickTextCollection { Name = "General" };
            fallback.EnsureInitialized();
            _profile.QuickTextCollections.Add(fallback);
        }

        QuickTextCollections = new ObservableCollection<QuickTextCollection>(_profile.QuickTextCollections);
        LoadQuickTextCategories();
        string targetId = _profile.ActiveQuickTextCollectionId;
        if (string.IsNullOrWhiteSpace(targetId)
            || !QuickTextCollections.Any(collection => string.Equals(collection.Id, targetId, StringComparison.Ordinal)))
        {
            targetId = QuickTextCollections[0].Id;
            _profile.ActiveQuickTextCollectionId = targetId;
        }

        if (!string.Equals(SelectedQuickTextCollectionId, targetId, StringComparison.Ordinal))
            SelectedQuickTextCollectionId = targetId;
        else
        {
            RebuildOverlayQuickTextCategories();
            LoadQuickTextItemsForSelectedCategory();
        }

        NotifyQuickTextCollectionChanged();
    }

    private void LoadQuickTextCategories()
    {
        var categoryList = _profile.QuickTextCategories.ToList();

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

        RebuildOverlayQuickTextCategories();
    }

    private void RebuildOverlayQuickTextCategories()
    {
        string collectionId = SelectedQuickTextCollectionId ?? _profile.ActiveQuickTextCollectionId;
        var relevantTagIds = _profile.QuickTextItems
            .Where(item => item.HasCollection(collectionId))
            .SelectMany(item => item.CategoryIds)
            .ToHashSet(StringComparer.Ordinal);
        OverlayQuickTextCategories = new ObservableCollection<QuickTextCategory>(
            _profile.QuickTextCategories.Where(tag => relevantTagIds.Contains(tag.Id)));
    }

    private void LoadQuickTextItemsForSelectedCategory()
    {
        string categoryId = string.IsNullOrWhiteSpace(SelectedQuickTextCategoryId)
            ? _profile.ActiveQuickTextCategoryId
            : SelectedQuickTextCategoryId;

        string query = QuickTextSearchQuery?.Trim() ?? string.Empty;
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        var categoriesById = _profile.QuickTextCategories
            .GroupBy(category => category.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var quickTextViewModels = new List<QuickTextItemViewModel>();
        foreach (var item in _profile.QuickTextItems)
        {
            string tagSummary = BuildQuickTextTagSummary(item, categoriesById);

            string collectionId = SelectedQuickTextCollectionId ?? _profile.ActiveQuickTextCollectionId;
            if (!hasQuery && (!item.HasCollection(collectionId) || !item.HasCategory(categoryId)))
                continue;

            bool matchesText = (item.Text?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
            bool matchesCategory = tagSummary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            if (hasQuery && !matchesText && !matchesCategory)
                continue;

            quickTextViewModels.Add(new QuickTextItemViewModel(
                item,
                _profile.QuickTextCollections,
                _profile.QuickTextCategories,
                ScheduleAutoSave,
                (collection, isSelected) =>
                {
                    RebuildOverlayQuickTextCategories();
                    if (!isSelected && string.Equals(collection.Id, SelectedQuickTextCollectionId, StringComparison.Ordinal))
                        LoadQuickTextItemsForSelectedCategory();
                },
                (tag, isSelected) =>
                {
                    if (!isSelected && string.Equals(tag.Id, SelectedQuickTextCategoryId, StringComparison.Ordinal))
                        LoadQuickTextItemsForSelectedCategory();
                }));
        }

        QuickTextItems = new ObservableCollection<QuickTextItemViewModel>(quickTextViewModels);
        OnPropertyChanged(nameof(HasQuickTextItems));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
    }

    private static string BuildQuickTextTagSummary(
        QuickTextItem item,
        IReadOnlyDictionary<string, QuickTextCategory> categoriesById)
    {
        return string.Join(", ", item.CategoryIds.Select(id =>
        {
            if (!categoriesById.TryGetValue(id, out var category))
                return string.Empty;

            return category.Name;
        }).Where(label => !string.IsNullOrWhiteSpace(label)));
    }

    private void NotifyQuickTextCategoryChanged()
    {
        OnPropertyChanged(nameof(CurrentQuickTextCategoryName));
        OnPropertyChanged(nameof(QuickTextCategoryCount));
        OnPropertyChanged(nameof(HasMultipleQuickTextCategories));
    }

    private void NotifyQuickTextCollectionChanged()
    {
        OnPropertyChanged(nameof(CurrentQuickTextCollectionName));
        OnPropertyChanged(nameof(QuickTextCollectionCount));
        OnPropertyChanged(nameof(HasMultipleQuickTextCollections));
    }

    private string CreateUniqueQuickTextCategoryName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Tag" : baseName.Trim();

        if (_profile.QuickTextCategories.All(category =>
            !string.Equals(category.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int suffix = 2;
        while (_profile.QuickTextCategories.Any(category =>
            string.Equals(category.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;

        return $"{baseName} {suffix}";
    }

    [RelayCommand]
    private void AddQuickTextItem()
    {
        string categoryId = string.IsNullOrWhiteSpace(SelectedQuickTextCategoryId)
            ? _profile.ActiveQuickTextCategoryId
            : SelectedQuickTextCategoryId;
        string collectionId = SelectedQuickTextCollectionId ?? _profile.ActiveQuickTextCollectionId;

        var item = new QuickTextItem
        {
            Text = string.Empty,
            CategoryId = categoryId,
            CategoryIds = new List<string> { categoryId },
            CollectionIds = new List<string> { collectionId }
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
            CategoryId = source.Model.CategoryId,
            CategoryIds = source.Model.CategoryIds.ToList(),
            CollectionIds = source.Model.CollectionIds.ToList()
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
            Name = CreateUniqueQuickTextCategoryName($"Tag {QuickTextCategories.Count + 1}")
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
        if (QuickTextCategories.Count <= 1)
            return;

        string removeId = string.IsNullOrWhiteSpace(SelectedQuickTextCategoryId)
            ? _profile.ActiveQuickTextCategoryId
            : SelectedQuickTextCategoryId;

        int removeIndex = QuickTextCategories
            .Select((category, index) => new { category, index })
            .FirstOrDefault(entry => string.Equals(entry.category.Id, removeId, StringComparison.Ordinal))?.index ?? -1;
        if (removeIndex < 0)
            return;

        _profile.QuickTextCategories.RemoveAll(category => string.Equals(category.Id, removeId, StringComparison.Ordinal));
        QuickTextCategories.RemoveAt(removeIndex);

        string targetCategoryId = _profile.QuickTextCategories[Math.Clamp(removeIndex, 0, _profile.QuickTextCategories.Count - 1)].Id;

        foreach (var item in _profile.QuickTextItems)
        {
            if (!item.HasCategory(removeId))
                continue;

            item.SetCategories(item.CategoryIds
                .Select(id => string.Equals(id, removeId, StringComparison.Ordinal) ? targetCategoryId : id));
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

    private string CreateUniqueQuickTextCollectionName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Collection" : baseName.Trim();
        if (_profile.QuickTextCollections.All(collection =>
            !string.Equals(collection.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int suffix = 2;
        while (_profile.QuickTextCollections.Any(collection =>
            string.Equals(collection.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;

        return $"{baseName} {suffix}";
    }

    [RelayCommand]
    private void AddQuickTextCollection()
    {
        var collection = new QuickTextCollection
        {
            Name = CreateUniqueQuickTextCollectionName($"Collection {_profile.QuickTextCollections.Count + 1}")
        };
        collection.EnsureInitialized();
        _profile.QuickTextCollections.Add(collection);
        LoadQuickTextCollections();
        SelectedQuickTextCollectionId = collection.Id;
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveQuickTextCollection()
    {
        if (_profile.QuickTextCollections.Count <= 1 || CurrentQuickTextCollection == null)
            return;

        int removeIndex = _profile.QuickTextCollections.IndexOf(CurrentQuickTextCollection);
        var target = _profile.QuickTextCollections[Math.Clamp(removeIndex == 0 ? 1 : removeIndex - 1, 0, _profile.QuickTextCollections.Count - 1)];
        foreach (var item in _profile.QuickTextItems.Where(item => item.HasCollection(CurrentQuickTextCollection.Id)))
        {
            item.SetCollections(item.CollectionIds
                .Select(id => string.Equals(id, CurrentQuickTextCollection.Id, StringComparison.Ordinal) ? target.Id : id));
        }

        _profile.QuickTextCollections.Remove(CurrentQuickTextCollection);
        _profile.ActiveQuickTextCollectionId = target.Id;
        LoadQuickTextCollections();
        SelectedQuickTextCollectionId = target.Id;
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RenameQuickTextCollection(string? newName)
    {
        if (CurrentQuickTextCollection == null || string.IsNullOrWhiteSpace(newName))
            return;

        CurrentQuickTextCollection.Name = newName.Trim();
        LoadQuickTextCollections();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void SetQuickTextCollection(QuickTextCollection? collection)
    {
        if (collection == null || string.IsNullOrWhiteSpace(collection.Id))
            return;

        SelectedQuickTextCollectionId = collection.Id;
    }

    [RelayCommand]
    private void SetQuickTextCategory(QuickTextCategory? category)
    {
        if (category == null || string.IsNullOrWhiteSpace(category.Id))
            return;

        SelectedQuickTextCategoryId = category.Id;
    }

    public void MoveQuickTextCollection(QuickTextCollection source, QuickTextCollection target)
    {
        int sourceIndex = _profile.QuickTextCollections.IndexOf(source);
        int targetIndex = _profile.QuickTextCollections.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            return;

        _profile.QuickTextCollections.RemoveAt(sourceIndex);
        _profile.QuickTextCollections.Insert(targetIndex, source);
        LoadQuickTextCollections();
        SelectedQuickTextCollectionId = source.Id;
        ScheduleAutoSave();
    }

    public void MoveQuickTextCategory(QuickTextCategory source, QuickTextCategory target)
    {
        var orderedTags = _profile.QuickTextCategories.ToList();
        int sourceIndex = orderedTags.IndexOf(source);
        int targetIndex = orderedTags.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            return;

        orderedTags.RemoveAt(sourceIndex);
        orderedTags.Insert(targetIndex, source);
        _profile.QuickTextCategories = orderedTags;

        LoadQuickTextCategories();
        SelectedQuickTextCategoryId = source.Id;
        ScheduleAutoSave();
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

    [ObservableProperty]
    private string _quickTextExportScope = "Category"; // "Category" or "All"

    [ObservableProperty]
    private string _quickTextImportMode = "Replace"; // "Replace" or "Append"

    public bool IsExportScopeCategory => string.Equals(QuickTextExportScope, "Category", StringComparison.Ordinal);
    public bool IsExportScopeAll => string.Equals(QuickTextExportScope, "All", StringComparison.Ordinal);
    public bool IsImportModeReplace => string.Equals(QuickTextImportMode, "Replace", StringComparison.Ordinal);
    public bool IsImportModeAppend => string.Equals(QuickTextImportMode, "Append", StringComparison.Ordinal);

    partial void OnQuickTextExportScopeChanged(string value)
    {
        OnPropertyChanged(nameof(IsExportScopeCategory));
        OnPropertyChanged(nameof(IsExportScopeAll));
    }

    partial void OnQuickTextImportModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsImportModeReplace));
        OnPropertyChanged(nameof(IsImportModeAppend));
    }

    [RelayCommand]
    private void SetQuickTextExportScope(string? scope)
    {
        if (scope is "Category" or "All")
            QuickTextExportScope = scope;
    }

    [RelayCommand]
    private void SetQuickTextImportMode(string? mode)
    {
        if (mode is "Replace" or "Append")
            QuickTextImportMode = mode;
    }

    [RelayCommand]
    private void ExportQuickText()
    {
        bool exportAll = string.Equals(QuickTextExportScope, "All", StringComparison.Ordinal);
        var includedTags = exportAll
            ? _profile.QuickTextCategories.ToList()
            : CurrentQuickTextCategory is { } current ? new List<QuickTextCategory> { current } : new List<QuickTextCategory>();
        var includedTagIds = includedTags.Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
        var includedItems = _profile.QuickTextItems
            .Where(item => item.CategoryIds.Any(includedTagIds.Contains))
            .ToList();
        var includedCollectionIds = includedItems
            .SelectMany(item => item.CollectionIds)
            .ToHashSet(StringComparer.Ordinal);

        if (includedTags.Count == 0 || includedItems.Count == 0)
            return;

        var export = new QuickTextExport
        {
            Version = 2,
            Collections = _profile.QuickTextCollections
                .Where(collection => includedCollectionIds.Contains(collection.Id))
                .Select(collection => new QuickTextExportCollection { Key = collection.Id, Name = collection.Name })
                .ToList(),
            Tags = includedTags
                .Select(tag => new QuickTextExportTag { Key = tag.Id, Name = tag.Name })
                .ToList(),
            TaggedItems = includedItems
                .Select(item => new QuickTextTaggedExportItem
                {
                    Text = item.Text,
                    CollectionKeys = item.CollectionIds.Where(includedCollectionIds.Contains).ToList(),
                    TagKeys = item.CategoryIds.Where(includedTagIds.Contains).ToList()
                })
                .ToList(),
            // Keep the old flattened representation so older StreamDecky builds can still import the export.
            Categories = includedTags
                .Select(tag => new QuickTextExportCategory
                {
                    Name = tag.Name,
                    Items = includedItems
                        .Where(item => item.HasCategory(tag.Id))
                        .Select(item => new QuickTextExportItem { Text = item.Text })
                        .ToList()
                })
                .ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        string json = JsonSerializer.Serialize(export, options);
        System.Windows.Clipboard.SetText(json);
    }

    [RelayCommand]
    private void ImportQuickText()
    {
        string json;
        try
        {
            json = System.Windows.Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(json))
                return;
        }
        catch
        {
            return;
        }

        QuickTextExport? import;
        try
        {
            import = JsonSerializer.Deserialize<QuickTextExport>(json);
        }
        catch
        {
            return;
        }

        if (import == null)
            return;

        bool replace = string.Equals(QuickTextImportMode, "Replace", StringComparison.Ordinal);
        bool scopeIsCategory = string.Equals(QuickTextExportScope, "Category", StringComparison.Ordinal);
        bool hasStructuredData = import.Version >= 2 && import.Tags.Count > 0 && import.TaggedItems.Count > 0;

        if (hasStructuredData && !(replace && scopeIsCategory))
        {
            ImportStructuredQuickText(import, replace);
            return;
        }

        if (hasStructuredData && replace && scopeIsCategory)
        {
            var importedTag = import.Tags[0];
            import.Categories =
            [
                new QuickTextExportCategory
                {
                    Name = importedTag.Name,
                    Items = import.TaggedItems
                        .Where(item => item.TagKeys.Contains(importedTag.Key, StringComparer.Ordinal))
                        .Select(item => new QuickTextExportItem { Text = item.Text })
                        .ToList()
                }
            ];
        }

        if (import.Categories.Count == 0)
            return;

        if (replace && scopeIsCategory)
        {
            // Replace just the current tag
            var current = CurrentQuickTextCategory;
            if (current == null)
                return;

            int oldItemCount = _profile.QuickTextItems.Count(i => i.HasCategory(current.Id));
            var importCat = import.Categories[0];
            int newItemCount = importCat.Items?.Count ?? 0;

            if (oldItemCount > 0)
            {
                bool confirmed = Helpers.ConfirmDialog.Show(
                    System.Windows.Application.Current?.MainWindow,
                    "Import QuickText",
                    $"Replace all items tagged \"{current.Name}\"?\n\nShared items keep their other tags. This tag will receive {newItemCount} item(s) from the JSON.",
                    confirmText: "Replace",
                    danger: true);

                if (!confirmed)
                    return;
            }

            // Remove the current tag; only delete items that have no other tag.
            foreach (var existingItem in _profile.QuickTextItems.Where(item => item.HasCategory(current.Id)).ToList())
            {
                if (existingItem.CategoryIds.Count == 1)
                    _profile.QuickTextItems.Remove(existingItem);
                else
                    existingItem.SetCategories(existingItem.CategoryIds.Where(id => !string.Equals(id, current.Id, StringComparison.Ordinal)));
            }

            // Use the import category name or keep current
            if (!string.IsNullOrWhiteSpace(importCat.Name) && !string.Equals(importCat.Name, current.Name, StringComparison.Ordinal))
                current.Name = importCat.Name.Trim();

            if (importCat.Items != null)
            {
                foreach (var item in importCat.Items)
                {
                    if (string.IsNullOrWhiteSpace(item?.Text))
                        continue;
                    var newItem = new QuickTextItem
                    {
                        Text = item.Text,
                        CategoryId = current.Id,
                        CategoryIds = new List<string> { current.Id },
                        CollectionIds = new List<string> { SelectedQuickTextCollectionId ?? _profile.ActiveQuickTextCollectionId }
                    };
                    newItem.EnsureInitialized();
                    _profile.QuickTextItems.Add(newItem);
                }
            }

            LoadQuickTextCategories();
            SelectedQuickTextCategoryId = current.Id;
        }
        else if (replace)
        {
            // Replace all — destructive, warn
            int allItemCount = _profile.QuickTextItems.Count;
            int allCatCount = _profile.QuickTextCategories.Count;

            bool confirmed = Helpers.ConfirmDialog.Show(
                System.Windows.Application.Current?.MainWindow,
                "Import QuickText — Replace All",
                $"Replace ALL QuickText data?\n\nThis will remove ALL {allCatCount} tag(s) and {allItemCount} item(s) and replace them with the JSON content.",
                confirmText: "Replace All",
                danger: true);

            if (!confirmed)
                return;

            _profile.QuickTextCategories.Clear();
            QuickTextCategories.Clear();
            _profile.QuickTextItems.Clear();
            _profile.QuickTextCollections.Clear();
            var importedCollection = new QuickTextCollection { Name = "Imported" };
            importedCollection.EnsureInitialized();
            _profile.QuickTextCollections.Add(importedCollection);
            _profile.ActiveQuickTextCollectionId = importedCollection.Id;

            foreach (var cat in import.Categories)
                ImportCategoryItems(cat, cat.Name.Trim());

            SelectedQuickTextCategoryId = _profile.QuickTextCategories[^1].Id;
        }
        else
        {
            // Append
            foreach (var cat in import.Categories)
            {
                string catName = string.IsNullOrWhiteSpace(cat.Name) ? "Imported" : cat.Name.Trim();
                ImportCategoryItems(cat, CreateUniqueQuickTextCategoryName(catName));
            }

            SelectedQuickTextCategoryId = _profile.QuickTextCategories[^1].Id;
        }

        LoadQuickTextCollections();
        LoadQuickTextItemsForSelectedCategory();
        NotifyQuickTextCategoryChanged();
        OnPropertyChanged(nameof(HasQuickTextItems));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        ScheduleAutoSave();
    }

    private void ImportStructuredQuickText(QuickTextExport import, bool replace)
    {
        if (replace)
        {
            bool confirmed = Helpers.ConfirmDialog.Show(
                System.Windows.Application.Current?.MainWindow,
                "Import QuickText — Replace All",
                $"Replace ALL QuickText data?\n\nThis will remove {_profile.QuickTextCollections.Count} collection(s), {_profile.QuickTextCategories.Count} tag(s), and {_profile.QuickTextItems.Count} item(s).",
                confirmText: "Replace All",
                danger: true);
            if (!confirmed)
                return;

            _profile.QuickTextCollections.Clear();
            _profile.QuickTextCategories.Clear();
            _profile.QuickTextItems.Clear();
        }

        var collectionMap = new Dictionary<string, QuickTextCollection>(StringComparer.Ordinal);
        foreach (var importedCollection in import.Collections)
        {
            var collection = new QuickTextCollection
            {
                Name = replace
                    ? importedCollection.Name
                    : CreateUniqueQuickTextCollectionName(importedCollection.Name)
            };
            collection.EnsureInitialized();
            _profile.QuickTextCollections.Add(collection);
            collectionMap[importedCollection.Key] = collection;
        }

        if (collectionMap.Count == 0)
        {
            var fallback = new QuickTextCollection { Name = CreateUniqueQuickTextCollectionName("Imported") };
            fallback.EnsureInitialized();
            _profile.QuickTextCollections.Add(fallback);
            collectionMap[string.Empty] = fallback;
        }

        var tagMap = new Dictionary<string, QuickTextCategory>(StringComparer.Ordinal);
        foreach (var importedTag in import.Tags)
        {
            var tag = new QuickTextCategory
            {
                Name = importedTag.Name
            };
            tag.EnsureInitialized();
            _profile.QuickTextCategories.Add(tag);
            tagMap[importedTag.Key] = tag;
        }

        foreach (var importedItem in import.TaggedItems)
        {
            var tagIds = importedItem.TagKeys
                .Where(tagMap.ContainsKey)
                .Select(key => tagMap[key].Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var collectionIds = importedItem.CollectionKeys
                .Where(collectionMap.ContainsKey)
                .Select(key => collectionMap[key].Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (collectionIds.Count == 0)
                collectionIds.Add(collectionMap.Values.First().Id);

            if (string.IsNullOrWhiteSpace(importedItem.Text) || tagIds.Count == 0)
                continue;

            var item = new QuickTextItem { Text = importedItem.Text };
            item.SetCollections(collectionIds);
            item.SetCategories(tagIds);
            item.EnsureInitialized();
            _profile.QuickTextItems.Add(item);
        }

        var firstCollection = collectionMap.Values.First();
        _profile.ActiveQuickTextCollectionId = firstCollection.Id;
        _profile.ActiveQuickTextCategoryId = tagMap.Values.FirstOrDefault()?.Id ?? string.Empty;
        LoadQuickTextCollections();
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        ScheduleAutoSave();
    }

    private void ImportCategoryItems(QuickTextExportCategory importCat, string categoryName)
    {
        var newCategory = new QuickTextCategory
        {
            Name = categoryName
        };
        newCategory.EnsureInitialized();
        _profile.QuickTextCategories.Add(newCategory);
        QuickTextCategories.Add(newCategory);

        if (importCat.Items == null)
            return;

        foreach (var item in importCat.Items)
        {
            if (string.IsNullOrWhiteSpace(item?.Text))
                continue;
            var newItem = new QuickTextItem
            {
                Text = item.Text,
                CategoryId = newCategory.Id,
                CategoryIds = new List<string> { newCategory.Id },
                CollectionIds = new List<string> { _profile.ActiveQuickTextCollectionId }
            };
            newItem.EnsureInitialized();
            _profile.QuickTextItems.Add(newItem);
        }
    }

    private sealed class QuickTextExport
    {
        public int Version { get; set; }
        public List<QuickTextExportCollection> Collections { get; set; } = new();
        public List<QuickTextExportTag> Tags { get; set; } = new();
        public List<QuickTextTaggedExportItem> TaggedItems { get; set; } = new();
        public List<QuickTextExportCategory> Categories { get; set; } = new();
    }

    private sealed class QuickTextExportCollection
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class QuickTextExportTag
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class QuickTextTaggedExportItem
    {
        public string Text { get; set; } = string.Empty;
        public List<string> CollectionKeys { get; set; } = new();
        public List<string> TagKeys { get; set; } = new();
    }

    private sealed class QuickTextExportCategory
    {
        public string Name { get; set; } = string.Empty;
        public List<QuickTextExportItem> Items { get; set; } = new();
    }

    private sealed class QuickTextExportItem
    {
        public string Text { get; set; } = string.Empty;
    }
}
